using UnityEngine;

namespace RedLightQwop
{
    /// <summary>
    /// A driven ConfigurableJoint. Call SetTargetEuler with the desired rotation of this body
    /// part relative to the part it is attached to, in degrees. The joint's slerp drive then
    /// applies torque to reach it, like a muscle pulling a limb toward a pose.
    /// </summary>
    [RequireComponent(typeof(ConfigurableJoint))]
    public class Muscle : MonoBehaviour
    {
        public string Label;

        [Header("Drive")]
        public float Spring = 1000f;
        public float Damper = 80f;
        public float MaxForce = 100000f;

        public ConfigurableJoint Joint { get; private set; }
        public Vector3 TargetEuler { get; private set; }

        Rigidbody m_ParentBody;
        Quaternion m_StartLocalRotation;

        void Awake()
        {
            Joint = GetComponent<ConfigurableJoint>();
            m_ParentBody = Joint.connectedBody;
            m_StartLocalRotation = LocalRotationRelativeToParent();
            ApplyDrive();
        }

        Quaternion LocalRotationRelativeToParent()
        {
            if (m_ParentBody == null) return transform.localRotation;
            return Quaternion.Inverse(m_ParentBody.transform.rotation) * transform.rotation;
        }

        public void ApplyDrive()
        {
            Joint.rotationDriveMode = RotationDriveMode.Slerp;
            Joint.slerpDrive = new JointDrive
            {
                positionSpring = Spring,
                positionDamper = Damper,
                maximumForce = MaxForce,
            };
        }

        /// <summary>Desired rotation relative to the parent part, in degrees (X = pitch forward/back).</summary>
        public void SetTargetEuler(Vector3 euler)
        {
            TargetEuler = euler;
            Quaternion target = Quaternion.Euler(euler) * m_StartLocalRotation;
            Joint.SetTargetRotationLocal(target, m_StartLocalRotation);
        }
    }
}
