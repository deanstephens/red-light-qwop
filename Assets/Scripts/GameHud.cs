using UnityEngine;
using UnityEngine.UI;

namespace RedLightQwop
{
    /// <summary>Simple uGUI HUD: phase banner, distance/time, end-of-game message, controls hint.</summary>
    public class GameHud : MonoBehaviour
    {
        public Text PhaseText;
        public Text InfoText;
        public Text CenterText;
        public Text HintText;

        public Color GreenColor = new Color(0.3f, 1f, 0.4f);
        public Color RedColor = new Color(1f, 0.3f, 0.25f);

        public void Refresh(GameManager game)
        {
            if (PhaseText != null)
            {
                bool red = game.Phase == LightPhase.Red;
                PhaseText.text = red ? "RED LIGHT" : "GREEN LIGHT";
                PhaseText.color = red ? RedColor : GreenColor;
            }

            if (InfoText != null)
            {
                string runners = game.NpcCount > 0 ? $"     runners {game.NpcActiveCount}/{game.NpcCount}" : "";
                InfoText.text = $"{game.DistanceToFinish:0.0} m to go     {Mathf.CeilToInt(game.TimeRemaining)} s{runners}";
            }

            if (CenterText != null)
            {
                switch (game.State)
                {
                    case GameState.Won:
                        CenterText.text = "YOU MADE IT!\n<size=28>Press R to play again</size>";
                        break;
                    case GameState.Eliminated:
                        CenterText.text = "ELIMINATED\n<size=28>You moved on red. Press R to retry</size>";
                        break;
                    case GameState.TimedOut:
                        CenterText.text = "TIME'S UP\n<size=28>Press R to retry</size>";
                        break;
                    default:
                        CenterText.text = game.SessionActive && game.Player == null ? "Connecting..." : "";
                        break;
                }
            }

            if (HintText != null)
            {
                string session = string.IsNullOrEmpty(game.SessionLabel) ? "" : $"      |      {game.SessionLabel}, {game.Players.Count} player{(game.Players.Count == 1 ? "" : "s")}";
                HintText.text = $"Q / W  hips      O / P  knees      R  restart{session}";
            }
        }
    }
}
