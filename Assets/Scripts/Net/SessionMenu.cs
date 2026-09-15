using System.Net;
using UnityEngine;
using UnityEngine.UI;

namespace RedLightQwop
{
    /// <summary>
    /// Start-of-game panel. Solo and LAN hosting connect directly; "Host online" creates a
    /// relayed session with a join code that works across the internet without port
    /// forwarding. The Join field accepts either an IP address or a join code.
    /// </summary>
    public class SessionMenu : MonoBehaviour
    {
        public GameObject Panel;
        public Button SoloButton;
        public Button HostButton;
        public Button HostOnlineButton;
        public Button JoinButton;
        public InputField AddressField;
        public Text StatusText;

        public bool IsOpen => Panel != null && Panel.activeSelf;

        void Start()
        {
            var game = GameManager.Instance;
            if (game == null) return;
            game.SessionStatusChanged += Status;

            if (SoloButton != null) SoloButton.onClick.AddListener(() => { if (!game.StartSolo()) Status("Could not start"); });
            if (HostButton != null) HostButton.onClick.AddListener(() => { if (!game.StartHost()) Status("Could not host"); });
            if (HostOnlineButton != null) HostOnlineButton.onClick.AddListener(() => { _ = game.StartHostRelayAsync(); });
            if (JoinButton != null) JoinButton.onClick.AddListener(() =>
            {
                string text = AddressField != null ? AddressField.text.Trim() : "";
                if (text.Length == 0) { Status("Enter a host address or a join code"); return; }
                if (LooksLikeAddress(text))
                {
                    if (!game.StartClient(text)) Status("Could not connect");
                }
                else
                {
                    _ = game.StartClientRelayAsync(text);
                }
            });
            if (GameManager.IsWebBuild)
            {
                // No LAN hosting in a browser; solo and host both create an online session.
                if (HostButton != null) HostButton.gameObject.SetActive(false);
                if (SoloButton != null) SetLabel(SoloButton, "Play solo (online)");
                if (AddressField != null && AddressField.placeholder is Text ph) ph.text = "join code";
                if (StatusText != null) StatusText.text = "Runs in your browser through the online relay";
            }
            else if (StatusText != null)
            {
                StatusText.text = $"Your LAN address: {GameManager.LocalIPv4()}";
            }
        }

        static void SetLabel(Button button, string text)
        {
            var label = button.GetComponentInChildren<Text>();
            if (label != null) label.text = text;
        }

        void OnDestroy()
        {
            var game = GameManager.Instance;
            if (game != null) game.SessionStatusChanged -= Status;
        }

        static bool LooksLikeAddress(string text)
        {
            return text.Contains(".") || text.Contains(":") || IPAddress.TryParse(text, out _) || text.Equals("localhost", System.StringComparison.OrdinalIgnoreCase);
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
