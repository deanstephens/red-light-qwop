using UnityEngine;

namespace RedLightQwop
{
    /// <summary>Trigger volume at the end of the course. Any player body part crossing it wins.</summary>
    [RequireComponent(typeof(Collider))]
    public class FinishLine : MonoBehaviour
    {
        public GameManager Game;

        void OnTriggerEnter(Collider other)
        {
            if (Game == null) Game = FindAnyObjectByType<GameManager>();
            if (Game != null) Game.OnFinishReached(other.attachedRigidbody);
        }
    }
}
