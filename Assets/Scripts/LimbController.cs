using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RedLightQwop
{
    /// <summary>
    /// Per-limb input, each channel in the range -1..1.
    /// Hips: +1 swings the thigh forward, -1 swings it back.
    /// Knees: +1 bends the knee, 0 or less straightens it.
    /// </summary>
    [System.Serializable]
    public struct LimbInput : INetworkSerializable, System.IEquatable<LimbInput>
    {
        public float LeftHip;
        public float RightHip;
        public float LeftKnee;
        public float RightKnee;
        /// <summary>Optional explicit steering, -1..1. Positive turns right. NPC wander uses it.</summary>
        public float Steer;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref LeftHip);
            serializer.SerializeValue(ref RightHip);
            serializer.SerializeValue(ref LeftKnee);
            serializer.SerializeValue(ref RightKnee);
            serializer.SerializeValue(ref Steer);
        }

        public bool Same(in LimbInput o)
        {
            return LeftHip == o.LeftHip && RightHip == o.RightHip && LeftKnee == o.LeftKnee && RightKnee == o.RightKnee && Steer == o.Steer;
        }

        public bool Equals(LimbInput other) => Same(other);
        public override bool Equals(object obj) => obj is LimbInput other && Same(other);
        public override int GetHashCode() => System.HashCode.Combine(LeftHip, RightHip, LeftKnee, RightKnee, Steer);
    }

    /// <summary>
    /// Reads the keyboard (Q/W for hips, O/P for knees, QWOP-style) and turns it into muscle
    /// targets on the ragdoll. Tests and other systems can disable the keyboard and set
    /// <see cref="Current"/> directly.
    /// </summary>
    public class LimbController : MonoBehaviour
    {
        public Ragdoll Ragdoll;

        [Header("Ranges (degrees)")]
        public float HipForwardAngle = 70f;
        public float HipBackAngle = 35f;
        public float KneeBendAngle = 100f;
        public float ArmSwingAngle = 40f;

        [Header("Turning")]
        [Tooltip("Yaw applied to both hip joints at full steer, in degrees. Twisting the planted thigh turns the body.")]
        public float HipTurnAngle = 30f;
        [Tooltip("How much a lopsided hip swing (one leg far forward) steers by itself. 0 keeps the legs straight unless Steer is set.")]
        [Range(0f, 1f)] public float SwingSteer = 0.6f;
        [Tooltip("Flip if positive steer turns the doll left in practice.")]
        public float SteerSign = 1f;

        [Header("Input")]
        public bool InputEnabled = true;
        public bool UseKeyboard = true;
        public LimbInput Current;

        void Update()
        {
            if (UseKeyboard) ReadKeyboard();
        }

        void FixedUpdate()
        {
            var input = InputEnabled ? Current : default;
            Apply(input);
            if (Ragdoll != null) Ragdoll.SetBraking(IsNeutral(input));
        }

        static bool IsNeutral(LimbInput i)
        {
            return Mathf.Approximately(i.LeftHip, 0f) && Mathf.Approximately(i.RightHip, 0f)
                && Mathf.Approximately(i.LeftKnee, 0f) && Mathf.Approximately(i.RightKnee, 0f)
                && Mathf.Approximately(i.Steer, 0f);
        }

        void ReadKeyboard()
        {
            Current = ReadKeyboardInput();
        }

        /// <summary>QWOP coupling: each key drives one leg one way and the other leg the opposite way.</summary>
        public static LimbInput ReadKeyboardInput()
        {
            var k = Keyboard.current;
            if (k == null) return default;

            float q = k.qKey.isPressed ? 1f : 0f;
            float w = k.wKey.isPressed ? 1f : 0f;
            float o = k.oKey.isPressed ? 1f : 0f;
            float p = k.pKey.isPressed ? 1f : 0f;

            return new LimbInput
            {
                LeftHip = q - w,
                RightHip = w - q,
                LeftKnee = o - p,
                RightKnee = p - o,
            };
        }

        void Apply(LimbInput input)
        {
            if (Ragdoll == null) return;

            // Big lopsided swings twist the hips, so the doll drifts off a straight line.
            float steer = input.Steer + SwingSteer * 0.5f * (input.LeftHip - input.RightHip);
            float hipYaw = SteerSign * Mathf.Clamp(steer, -1f, 1f) * HipTurnAngle;

            // Positive X rotation swings a downward-hanging limb backward, so forward is negative.
            SetHip(Ragdoll.LeftHip, -HipAngle(input.LeftHip), hipYaw);
            SetHip(Ragdoll.RightHip, -HipAngle(input.RightHip), hipYaw);
            SetPitch(Ragdoll.LeftKnee, KneeAngle(input.LeftKnee));
            SetPitch(Ragdoll.RightKnee, KneeAngle(input.RightKnee));

            // Arms counter-swing the hips for a natural look and a bit of balance.
            SetPitch(Ragdoll.LeftShoulder, -Mathf.Clamp(input.RightHip, -1f, 1f) * ArmSwingAngle);
            SetPitch(Ragdoll.RightShoulder, -Mathf.Clamp(input.LeftHip, -1f, 1f) * ArmSwingAngle);
            SetPitch(Ragdoll.LeftElbow, -20f);
            SetPitch(Ragdoll.RightElbow, -20f);
            SetPitch(Ragdoll.Spine, 0f);
            SetPitch(Ragdoll.Neck, 0f);
        }

        float HipAngle(float v)
        {
            v = Mathf.Clamp(v, -1f, 1f);
            return v >= 0f ? v * HipForwardAngle : v * HipBackAngle;
        }

        float KneeAngle(float v)
        {
            return Mathf.Clamp01(v) * KneeBendAngle;
        }

        static void SetPitch(Muscle muscle, float degrees)
        {
            if (muscle != null) muscle.SetTargetEuler(new Vector3(degrees, 0f, 0f));
        }

        static void SetHip(Muscle muscle, float pitch, float yaw)
        {
            if (muscle != null) muscle.SetTargetEuler(new Vector3(pitch, yaw, 0f));
        }
    }
}
