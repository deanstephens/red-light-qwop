using UnityEngine;
using UnityEngine.UI;

namespace RedLightQwop
{
    /// <summary>Start-of-game panel: play solo, host, or join a host by address.</summary>
    public class SessionMenu : MonoBehaviour
    {
        public GameObject Panel;
        public Button SoloButton;
        public Button HostButton;
        public Button JoinButton;
        public InputField AddressField;
        public Text StatusText;

        public bool IsOpen => Panel != null && Panel.activeSelf;

        void Start()
        {
            var game = GameManager.Instance;
            if (SoloButton != null) SoloButton.onClick.AddListener(() => { if (!game.StartSolo()) Status("Could not start"); });
            if (HostButton != null) HostButton.onClick.AddListener(() => { if (!game.StartHost()) Status("Could not host"); });
            if (JoinButton != null) JoinButton.onClick.AddListener(() =>
            {
                string addr = AddressField != null && !string.IsNullOrWhiteSpace(AddressField.text) ? AddressField.text.Trim() : "127.0.0.1";
                if (!game.StartClient(addr)) Status("Could not connect");
                else Status($"Connecting to {addr}...");
            });
            if (StatusText != null) StatusText.text = $"Your address: {GameManager.LocalIPv4()}";
        }

        public void Open(string status = null)
        {
            if (Panel != null) Panel.SetActive(true);
            if (status != null) Status(status);
        }

        public void Close()
        {
            if (Panel != null) Panel.SetActive(false);
        }

        void Status(string text)
        {
            if (StatusText != null) StatusText.text = text;
        }
    }
}
