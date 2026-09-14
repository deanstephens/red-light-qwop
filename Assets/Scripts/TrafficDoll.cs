using UnityEngine;

namespace RedLightQwop
{
    /// <summary>
    /// The doll at the finish line. Faces away from the player on green and turns around on
    /// red. Also drives the light indicator's color.
    /// </summary>
    public class TrafficDoll : MonoBehaviour
    {
        public Transform Body;
        public Renderer Indicator;
        public Light IndicatorLight;
        public float TurnSpeed = 360f;
        public Color GreenColor = new Color(0.2f, 0.9f, 0.3f);
        public Color RedColor = new Color(1f, 0.15f, 0.1f);

        static readonly int k_BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int k_EmissionColor = Shader.PropertyToID("_EmissionColor");

        public bool IsRed { get; private set; }
        Quaternion m_TargetRotation = Quaternion.identity;
        Material m_IndicatorMaterial;

        public void SetRed(bool isRed)
        {
            IsRed = isRed;
            m_TargetRotation = Quaternion.Euler(0f, isRed ? 180f : 0f, 0f);
            ApplyColor();
        }

        void Update()
        {
            if (Body == null) return;
            Body.localRotation = Quaternion.RotateTowards(Body.localRotation, m_TargetRotation, TurnSpeed * Time.deltaTime);
        }

        void ApplyColor()
        {
            Color c = IsRed ? RedColor : GreenColor;
            if (Indicator != null)
            {
                if (m_IndicatorMaterial == null) m_IndicatorMaterial = Indicator.material;
                m_IndicatorMaterial.SetColor(k_BaseColor, c);
                m_IndicatorMaterial.EnableKeyword("_EMISSION");
                m_IndicatorMaterial.SetColor(k_EmissionColor, c * 2f);
            }
            if (IndicatorLight != null) IndicatorLight.color = c;
        }
    }
}
