using Unity.Netcode;
using UnityEngine;

namespace RedLightQwop
{
    /// <summary>
    /// The shared round state, spawned by the host when the session starts. Clients read the
    /// phase and clock from here; the host writes them from GameManager.
    /// </summary>
    public class NetGameState : NetworkBehaviour
    {
        public NetworkVariable<int> Phase = new NetworkVariable<int>((int)LightPhase.Green);
        public NetworkVariable<float> TimeRemaining = new NetworkVariable<float>(0f);
        public NetworkVariable<bool> RoundRunning = new NetworkVariable<bool>(false);
        public NetworkVariable<int> Round = new NetworkVariable<int>(0);

        public LightPhase LightPhase => (LightPhase)Phase.Value;

        public override void OnNetworkSpawn()
        {
            var game = GameManager.Instance;
            if (game != null) game.AttachNetState(this);
        }

        public override void OnNetworkDespawn()
        {
            var game = GameManager.Instance;
            if (game != null) game.DetachNetState(this);
        }

        [Rpc(SendTo.Server)]
        public void RequestRestartRpc()
        {
            var game = GameManager.Instance;
            if (game != null) game.RestartRound();
        }
    }
}
