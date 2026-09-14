using UnityEngine;

namespace RedLightQwop
{
    /// <summary>
    /// The physics body of the player: eleven rigidbodies joined by muscles. Also provides a
    /// tunable upright assist so the doll does not instantly faceplant, and a few helpers the
    /// game logic needs (speed, position, teleport).
    /// </summary>
    public class Ragdoll : MonoBehaviour
    {
        [Header("Parts")]
        public Rigidbody Pelvis;
        public Rigidbody Torso;
        public Rigidbody Head;
        public Rigidbody LeftUpperLeg;
        public Rigidbody LeftLowerLeg;
        public Rigidbody RightUpperLeg;
        public Rigidbody RightLowerLeg;
        public Rigidbody LeftUpperArm;
        public Rigidbody LeftLowerArm;
        public Rigidbody RightUpperArm;
        public Rigidbody RightLowerArm;

        [Header("Muscles")]
        public Muscle Spine;
        public Muscle Neck;
        public Muscle LeftHip;
        public Muscle RightHip;
        public Muscle LeftKnee;
        public Muscle RightKnee;
        public Muscle LeftShoulder;
        public Muscle RightShoulder;
        public Muscle LeftElbow;
        public Muscle RightElbow;

        [Header("Balance assist")]
        [Tooltip("Applies a torque that pulls the pelvis and torso toward upright and facing +Z. Lower it for a harder game.")]
        public bool BalanceAssist = true;
        public float UprightSpring = 900f;
        public float UprightDamper = 60f;
        [Range(0f, 1f)] public float TorsoShare = 0.5f;

        public Rigidbody[] Bodies { get; private set; }

        void Awake()
        {
            Bodies = GetComponentsInChildren<Rigidbody>();
            IgnoreSelfCollisions();
        }

        void IgnoreSelfCollisions()
        {
            var colliders = GetComponentsInChildren<Collider>();
            for (int i = 0; i < colliders.Length; i++)
            {
                for (int j = i + 1; j < colliders.Length; j++)
                {
                    Physics.IgnoreCollision(colliders[i], colliders[j], true);
                }
            }
        }

        void FixedUpdate()
        {
            if (!BalanceAssist) return;
            ApplyUpright(Pelvis, 1f - TorsoShare);
            ApplyUpright(Torso, TorsoShare);
        }

        void ApplyUpright(Rigidbody body, float share)
        {
            if (body == null || share <= 0f) return;

            // Rotation that would take the body back to identity (upright, facing +Z).
            Quaternion error = Quaternion.Inverse(body.rotation);
            error.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            if (float.IsNaN(axis.x) || float.IsInfinity(axis.x)) return;

            Vector3 torque = axis.normalized * (angle * Mathf.Deg2Rad * UprightSpring)
                             - body.angularVelocity * UprightDamper;
            body.AddTorque(torque * share, ForceMode.Force);
        }

        /// <summary>Speed of the pelvis, used for red-light movement detection.</summary>
        public float Speed => Pelvis != null ? Pelvis.linearVelocity.magnitude : 0f;

        public float AverageSpeed
        {
            get
            {
                if (Bodies == null || Bodies.Length == 0) return 0f;
                float sum = 0f;
                foreach (var b in Bodies) sum += b.linearVelocity.magnitude;
                return sum / Bodies.Length;
            }
        }

        public Vector3 Position => Pelvis != null ? Pelvis.position : transform.position;

        /// <summary>Move every part by the same offset and zero all velocities.</summary>
        public void Teleport(Vector3 delta)
        {
            foreach (var b in Bodies)
            {
                b.position += delta;
                b.transform.position += delta;
                b.linearVelocity = Vector3.zero;
                b.angularVelocity = Vector3.zero;
            }
        }

        public void SetVelocity(Vector3 velocity)
        {
            foreach (var b in Bodies) b.linearVelocity = velocity;
        }
    }
}
