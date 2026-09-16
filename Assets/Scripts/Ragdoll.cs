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
        public float UprightDamper = 100f;
        [Range(0f, 1f)] public float TorsoShare = 0.5f;
        [Tooltip("How strongly the assist also pulls the doll back to facing down the course (+Z). 0 leaves heading entirely to the physics, so leg swings and bumps make it turn.")]
        [Range(0f, 1f)] public float YawAssist = 0f;

        [Header("Braking")]
        [Tooltip("Linear damping applied to every part while no limb input is held, so the doll stops quickly when the player freezes.")]
        public float BrakeDamping = 4f;
        public float RunDamping = 0.05f;
        public bool AllowBraking = true;

        public Rigidbody[] Bodies { get; private set; }
        public bool IsBraking { get; private set; }

        /// <summary>Current muscle and balance multipliers (1 = the prefab's tuning).</summary>
        public float MuscleScale { get; private set; } = 1f;
        public float BalanceScale { get; private set; } = 1f;
        float m_BaseUprightSpring;
        float m_BaseUprightDamper;

        Vector3[] m_InitialLocalPositions;
        Quaternion[] m_InitialLocalRotations;
        MeshRenderer[] m_Renderers;
        Material[] m_OriginalMaterials;

        void Awake()
        {
            Bodies = GetComponentsInChildren<Rigidbody>();
            IgnoreSelfCollisions();
            m_BaseUprightSpring = UprightSpring;
            m_BaseUprightDamper = UprightDamper;

            m_InitialLocalPositions = new Vector3[Bodies.Length];
            m_InitialLocalRotations = new Quaternion[Bodies.Length];
            for (int i = 0; i < Bodies.Length; i++)
            {
                m_InitialLocalPositions[i] = Bodies[i].transform.localPosition;
                m_InitialLocalRotations[i] = Bodies[i].transform.localRotation;
            }

            m_Renderers = GetComponentsInChildren<MeshRenderer>();
            m_OriginalMaterials = new Material[m_Renderers.Length];
            for (int i = 0; i < m_Renderers.Length; i++) m_OriginalMaterials[i] = m_Renderers[i].sharedMaterial;
        }

        /// <summary>
        /// Scale every muscle's drive and the balance assist. Lower values make the doll floppier
        /// and harder to control; this is what difficulty changes. Persists through Restore().
        /// </summary>
        public void ApplyStrength(float muscleScale, float balanceScale)
        {
            MuscleScale = Mathf.Max(0.05f, muscleScale);
            BalanceScale = Mathf.Max(0f, balanceScale);
            foreach (var muscle in GetComponentsInChildren<Muscle>())
            {
                muscle.Spring = muscle.OriginalSpring * MuscleScale;
                muscle.Damper = muscle.OriginalDamper * MuscleScale;
                muscle.ApplyDrive();
            }
            UprightSpring = m_BaseUprightSpring * BalanceScale;
            UprightDamper = m_BaseUprightDamper * Mathf.Max(0.4f, BalanceScale);
        }

        /// <summary>Undo GoLimp and Tint, and put the doll back in its spawn pose at rest.</summary>
        public void Restore()
        {
            ApplyStrength(MuscleScale, BalanceScale);
            BalanceAssist = true;
            AllowBraking = true;
            IsBraking = true;
            SetBraking(false);
            RestoreTint();
            ResetPose();
        }

        public void ResetPose()
        {
            for (int i = 0; i < Bodies.Length; i++)
            {
                var b = Bodies[i];
                b.linearVelocity = Vector3.zero;
                b.angularVelocity = Vector3.zero;
                b.transform.localPosition = m_InitialLocalPositions[i];
                b.transform.localRotation = m_InitialLocalRotations[i];
                b.position = b.transform.position;
                b.rotation = b.transform.rotation;
            }
            Physics.SyncTransforms();
        }

        public void RestoreTint()
        {
            if (m_Renderers == null) return;
            for (int i = 0; i < m_Renderers.Length; i++)
            {
                if (m_Renderers[i] != null) m_Renderers[i].sharedMaterial = m_OriginalMaterials[i];
            }
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

            Vector3 torque = Vector3.zero;

            // Tilt: the smallest rotation that brings the body's up axis back to world up.
            Vector3 bodyUp = body.rotation * Vector3.up;
            Quaternion tiltFix = Quaternion.FromToRotation(bodyUp, Vector3.up);
            tiltFix.ToAngleAxis(out float tiltAngle, out Vector3 tiltAxis);
            if (tiltAngle > 180f) tiltAngle -= 360f;
            if (Mathf.Abs(tiltAngle) > 0.01f && !float.IsNaN(tiltAxis.x) && !float.IsInfinity(tiltAxis.x))
            {
                torque += tiltAxis.normalized * (tiltAngle * Mathf.Deg2Rad * UprightSpring);
            }

            // Heading: optional pull toward +Z about the world up axis.
            if (YawAssist > 0f)
            {
                Vector3 forward = Vector3.ProjectOnPlane(tiltFix * (body.rotation * Vector3.forward), Vector3.up);
                if (forward.sqrMagnitude > 1e-4f)
                {
                    float yaw = Vector3.SignedAngle(forward, Vector3.forward, Vector3.up);
                    torque += Vector3.up * (yaw * Mathf.Deg2Rad * UprightSpring * YawAssist);
                }
            }

            torque -= body.angularVelocity * UprightDamper;
            body.AddTorque(torque * share, ForceMode.Force);
        }

        /// <summary>Heading of the pelvis in degrees: 0 faces down the course, positive turns right.</summary>
        public float Heading
        {
            get
            {
                Vector3 forward = Vector3.ProjectOnPlane(Pelvis.rotation * Vector3.forward, Vector3.up);
                if (forward.sqrMagnitude < 1e-4f) return 0f;
                return Vector3.SignedAngle(Vector3.forward, forward, Vector3.up);
            }
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

        /// <summary>Switch between run damping and brake damping on every part.</summary>
        public void SetBraking(bool braking)
        {
            braking = braking && AllowBraking;
            if (braking == IsBraking) return;
            IsBraking = braking;
            float d = braking ? BrakeDamping : RunDamping;
            foreach (var b in Bodies) b.linearDamping = d;
        }

        /// <summary>Drop all muscle strength and the balance assist so the doll collapses.</summary>
        public void GoLimp(float spring = 20f, float damper = 5f)
        {
            BalanceAssist = false;
            AllowBraking = false;
            SetBraking(false);
            foreach (var muscle in GetComponentsInChildren<Muscle>())
            {
                muscle.Spring = spring;
                muscle.Damper = damper;
                muscle.ApplyDrive();
            }
        }

        /// <summary>Recolor every visual on this doll (instantiates materials). RestoreTint undoes it.</summary>
        public void Tint(Color color)
        {
            if (m_Renderers == null) m_Renderers = GetComponentsInChildren<MeshRenderer>();
            foreach (var r in m_Renderers)
            {
                if (r != null) r.material.SetColor("_BaseColor", color);
            }
        }

        /// <summary>Put every part of the doll on the given physics layer.</summary>
        public void SetLayer(int layer)
        {
            foreach (var t in GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        }
    }
}
