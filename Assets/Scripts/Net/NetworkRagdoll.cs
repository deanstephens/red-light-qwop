using Unity.Netcode;
using UnityEngine;

namespace RedLightQwop
{
    /// <summary>
    /// Network identity of one doll. The host simulates every doll; clients receive body-part
    /// transforms through NetworkTransform/NetworkRigidbody on each part. A player's owner
    /// writes its limb input into a NetworkVariable which the host applies. State (playing,
    /// eliminated, won) is host-written and drives the tint on every peer.
    /// </summary>
    public class NetworkRagdoll : NetworkBehaviour
    {
        public bool IsNpc;
        public Ragdoll Ragdoll;
        public LimbController Controller;
        public NpcBrain Brain;
        public Color EliminatedTint = new Color(0.45f, 0.4f, 0.4f);

        public NetworkVariable<LimbInput> OwnerInput = new NetworkVariable<LimbInput>(default,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        public NetworkVariable<int> StateVar = new NetworkVariable<int>((int)GameState.Playing);
        public NetworkVariable<int> NpcStateVar = new NetworkVariable<int>((int)NpcBrain.NpcState.Active);

        public GameState State => (GameState)StateVar.Value;
        public NpcBrain.NpcState NpcState => (NpcBrain.NpcState)NpcStateVar.Value;

        /// <summary>Server-side scratch for red-light judging.</summary>
        public float MovingTime;

        void Awake()
        {
            if (Ragdoll == null) Ragdoll = GetComponent<Ragdoll>();
            if (Controller == null) Controller = GetComponent<LimbController>();
            if (Brain == null) Brain = GetComponent<NpcBrain>();
        }

        public override void OnNetworkSpawn()
        {
            bool simulate = IsServer;
            Ragdoll.enabled = simulate;
            Controller.enabled = simulate;
            Controller.UseKeyboard = false;
            if (Brain != null) Brain.enabled = simulate && IsNpc;

            StateVar.OnValueChanged += OnStateChanged;
            NpcStateVar.OnValueChanged += OnNpcStateChanged;
            ApplyTint();

            var game = GameManager.Instance;
            if (game != null) game.Register(this);
        }

        public override void OnNetworkDespawn()
        {
            StateVar.OnValueChanged -= OnStateChanged;
            NpcStateVar.OnValueChanged -= OnNpcStateChanged;
            var game = GameManager.Instance;
            if (game != null) game.Unregister(this);
        }

        void Update()
        {
            if (!IsSpawned) return;

            if (!IsNpc && IsOwner)
            {
                var game = GameManager.Instance;
                var input = (game == null || game.LocalInputAllowed) ? LimbController.ReadKeyboardInput() : default;
                if (!input.Same(OwnerInput.Value)) OwnerInput.Value = input;
            }

            if (IsServer)
            {
                if (!IsNpc)
                {
                    Controller.Current = OwnerInput.Value;
                }
                else if (Brain != null)
                {
                    int s = (int)Brain.State;
                    if (NpcStateVar.Value != s) NpcStateVar.Value = s;
                }
            }
        }

        /// <summary>Server only. Applies the state to the simulation; peers follow via the variable.</summary>
        public void SetState(GameState state)
        {
            if (!IsServer) return;
            StateVar.Value = (int)state;
            switch (state)
            {
                case GameState.Playing:
                    Controller.InputEnabled = true;
                    break;
                case GameState.Eliminated:
                    Controller.InputEnabled = false;
                    Ragdoll.GoLimp();
                    break;
                default:
                    Controller.InputEnabled = false;
                    break;
            }
        }

        /// <summary>Server only. Fresh doll for a new round.</summary>
        public void ResetForRound()
        {
            if (!IsServer) return;
            MovingTime = 0f;
            Ragdoll.Restore();
            SetState(GameState.Playing);
        }

        void OnStateChanged(int previous, int current) => ApplyTint();
        void OnNpcStateChanged(int previous, int current) => ApplyTint();

        void ApplyTint()
        {
            bool eliminated = IsNpc ? NpcState == NpcBrain.NpcState.Eliminated : State == GameState.Eliminated;
            if (eliminated) Ragdoll.Tint(EliminatedTint);
            else Ragdoll.RestoreTint();
        }
    }
}
