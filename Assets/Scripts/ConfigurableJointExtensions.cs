using UnityEngine;

namespace RedLightQwop
{
    /// <summary>
    /// ConfigurableJoint.targetRotation is expressed in joint space and relative to the
    /// joint's starting orientation, which makes it awkward to drive directly. These helpers
    /// convert a desired local rotation (child relative to its connected body) into the value
    /// the joint expects.
    /// </summary>
    public static class ConfigurableJointExtensions
    {
        public static void SetTargetRotationLocal(this ConfigurableJoint joint, Quaternion targetLocalRotation, Quaternion startLocalRotation)
        {
            if (joint.configuredInWorldSpace)
            {
                Debug.LogError("SetTargetRotationLocal should not be used with joints configured in world space.", joint);
            }
            SetTargetRotationInternal(joint, targetLocalRotation, startLocalRotation, Space.Self);
        }

        public static void SetTargetRotation(this ConfigurableJoint joint, Quaternion targetWorldRotation, Quaternion startWorldRotation)
        {
            if (!joint.configuredInWorldSpace)
            {
                Debug.LogError("SetTargetRotation should be used with joints configured in world space.", joint);
            }
            SetTargetRotationInternal(joint, targetWorldRotation, startWorldRotation, Space.World);
        }

        static void SetTargetRotationInternal(ConfigurableJoint joint, Quaternion targetRotation, Quaternion startRotation, Space space)
        {
            // Build the joint-space frame from the joint's axes.
            Vector3 right = joint.axis;
            Vector3 forward = Vector3.Cross(joint.axis, joint.secondaryAxis).normalized;
            Vector3 up = Vector3.Cross(forward, right).normalized;
            Quaternion worldToJointSpace = Quaternion.LookRotation(forward, up);

            Quaternion result = Quaternion.Inverse(worldToJointSpace);
            if (space == Space.World)
            {
                result *= startRotation * Quaternion.Inverse(targetRotation);
            }
            else
            {
                result *= Quaternion.Inverse(targetRotation) * startRotation;
            }
            result *= worldToJointSpace;

            joint.targetRotation = result;
        }
    }
}
