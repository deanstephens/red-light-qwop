using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace RedLightQwop
{
    public enum LightPhase { Green, Red }
    public enum GameState { Playing, Won, Eliminated, TimedOut }

    /// <summary>
    /// Runs the Red Light, Green Light loop: alternates phases, watches the player's pelvis
    /// speed during red, and ends the game on a finish-line trigger, elimination, or timeout.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        [Header("References")]
        public Ragdoll Player;
        public LimbController Controller;
        public TrafficDoll Doll;
        public GameHud Hud;
        public Transform FinishLine;

        [Header("Rules")]
        [Tooltip("Min/max seconds a green phase lasts.")]
        public Vector2 GreenDuration = new Vector2(3f, 6f);
        [Tooltip("Min/max seconds a red phase lasts.")]
        public Vector2 RedDuration = new Vector2(2f, 4f);
        [Tooltip("Seconds after red starts before movement is judged (the doll is turning).")]
        public float RedGracePeriod = 0.8f;
        [Tooltip("Pelvis speed (m/s) that counts as moving during red.")]
        public float MoveThreshold = 0.4f;
        [Tooltip("Seconds of continuous movement during red before elimination.")]
        public float MoveTolerance = 0.15f;
        public float TimeLimit = 120f;
        [Tooltip("0 = random each run.")]
        public int RandomSeed = 0;
        [Tooltip("Physics step in seconds. Jointed ragdolls need a small step to stay stable.")]
        public float PhysicsTimestep = 0.01f;

        public LightPhase Phase { get; private set; }
        public GameState State { get; private set; }
        public float TimeRemaining { get; private set; }
        public float PhaseTimeLeft { get; private set; }
        public float PhaseElapsed { get; private set; }

        System.Random m_Rng;
        float m_MovingTime;
        bool m_PhaseLocked;

        public bool IsRedJudging => Phase == LightPhase.Red && PhaseElapsed >= RedGracePeriod;

        public const string PlayerLayerName = "Player";
        public const string NpcLayerName = "NPC";

        public System.Collections.Generic.List<NpcBrain> Npcs { get; } = new System.Collections.Generic.List<NpcBrain>();
        public int NpcCount => Npcs.Count;
        public int NpcActiveCount
        {
            get
            {
                int n = 0;
                foreach (var npc in Npcs) if (npc.State == NpcBrain.NpcState.Active) n++;
                return n;
            }
        }

        public float DistanceToFinish
        {
            get
            {
                if (FinishLine == null || Player == null) return 0f;
                return Mathf.Max(0f, FinishLine.position.z - Player.Position.z);
            }
        }

        void Awake()
        {
            if (PhysicsTimestep > 0f) Time.fixedDeltaTime = PhysicsTimestep;
            ConfigureLayerCollisions();
        }

        /// <summary>NPCs never collide with each other or with the player, only with the world.</summary>
        static void ConfigureLayerCollisions()
        {
            int npc = LayerMask.NameToLayer(NpcLayerName);
            int player = LayerMask.NameToLayer(PlayerLayerName);
            if (npc < 0) return;
            Physics.IgnoreLayerCollision(npc, npc, true);
            if (player >= 0) Physics.IgnoreLayerCollision(npc, player, true);
        }

        void Start()
        {
            m_Rng = RandomSeed == 0 ? new System.Random() : new System.Random(RandomSeed);
            Npcs.Clear();
            Npcs.AddRange(FindObjectsByType<NpcBrain>(FindObjectsSortMode.None));
            foreach (var npc in Npcs) npc.Game = this;
            TimeRemaining = TimeLimit;
            State = GameState.Playing;
            SetPhase(LightPhase.Green, Range(GreenDuration));
            if (Hud != null) Hud.Refresh(this);
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.rKey.wasPressedThisFrame)
            {
                Restart();
                return;
            }

            if (State != GameState.Playing) return;

            float dt = Time.deltaTime;
            TimeRemaining -= dt;
            if (TimeRemaining <= 0f)
            {
                TimeRemaining = 0f;
                End(GameState.TimedOut);
                return;
            }

            PhaseElapsed += dt;
            if (!m_PhaseLocked)
            {
                PhaseTimeLeft -= dt;
                if (PhaseTimeLeft <= 0f)
                {
                    if (Phase == LightPhase.Green) SetPhase(LightPhase.Red, Range(RedDuration));
                    else SetPhase(LightPhase.Green, Range(GreenDuration));
                }
            }

            if (IsRedJudging && Player != null)
            {
                if (Player.Speed > MoveThreshold)
                {
                    m_MovingTime += dt;
                    if (m_MovingTime >= MoveTolerance)
                    {
                        End(GameState.Eliminated);
                        return;
                    }
                }
                else
                {
                    m_MovingTime = 0f;
                }
            }

            if (Hud != null) Hud.Refresh(this);
        }

        float Range(Vector2 minMax)
        {
            return Mathf.Lerp(minMax.x, minMax.y, (float)m_Rng.NextDouble());
        }

        void SetPhase(LightPhase phase, float duration)
        {
            Phase = phase;
            PhaseTimeLeft = duration;
            PhaseElapsed = 0f;
            m_MovingTime = 0f;
            if (Doll != null) Doll.SetRed(phase == LightPhase.Red);
        }

        /// <summary>Hold a phase indefinitely. Used by tests and for debugging.</summary>
        public void ForcePhase(LightPhase phase)
        {
            if (m_Rng == null) m_Rng = new System.Random();
            SetPhase(phase, float.PositiveInfinity);
            m_PhaseLocked = true;
        }

        public void ReleasePhase()
        {
            m_PhaseLocked = false;
            SetPhase(Phase, Range(Phase == LightPhase.Green ? GreenDuration : RedDuration));
        }

        public void OnFinishReached(Rigidbody body)
        {
            if (State != GameState.Playing || Player == null || body == null) return;
            if (Player.Bodies != null && System.Array.IndexOf(Player.Bodies, body) >= 0)
            {
                End(GameState.Won);
            }
        }

        public Color EliminatedTint = new Color(0.45f, 0.4f, 0.4f);

        void End(GameState state)
        {
            State = state;
            if (Controller != null) Controller.InputEnabled = false;
            if (state == GameState.Eliminated && Player != null)
            {
                Player.GoLimp();
                Player.Tint(EliminatedTint);
            }
            if (Hud != null) Hud.Refresh(this);
        }

        public void Restart()
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
    }
}
