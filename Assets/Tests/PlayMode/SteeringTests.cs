using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RedLightQwop.Tests
{
    public class SteeringTests
    {
        GameManager m_Game;

        static readonly LimbInput[] k_Stride =
        {
            new LimbInput { LeftHip = 1f, RightHip = -1f, LeftKnee = 1f, RightKnee = -1f },
            new LimbInput { LeftHip = 1f, RightHip = -1f, LeftKnee = -1f, RightKnee = 1f },
            new LimbInput { LeftHip = -1f, RightHip = 1f, LeftKnee = -1f, RightKnee = 1f },
            new LimbInput { LeftHip = -1f, RightHip = 1f, LeftKnee = 1f, RightKnee = -1f },
        };

        IEnumerator LoadGame()
        {
            yield return TestSession.Load(g => m_Game = g, parkRunners: true);
            m_Game.LocalPlayer.enabled = false; // tests drive Controller.Current directly
        }

        [UnityTearDown]
        public IEnumerator TearDown() => TestSession.Teardown();

        /// <summary>Walk the stride for the given time with a constant steer; returns heading change.</summary>
        IEnumerator Walk(float steer, float seconds, System.Action<float> onDone)
        {
            var ctl = m_Game.Controller;
            var rag = m_Game.Player;
            float startHeading = rag.Heading;
            float t = 0f, beat = 0f;
            int idx = 0;
            while (t < seconds)
            {
                yield return null;
                float dt = Time.deltaTime;
                t += dt;
                beat += dt;
                if (beat >= 0.3f) { beat = 0f; idx = (idx + 1) % k_Stride.Length; }
                var input = k_Stride[idx];
                input.Steer = steer;
                ctl.Current = input;
            }
            ctl.Current = default;
            onDone(Mathf.DeltaAngle(startHeading, rag.Heading));
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator PositiveSteerTurnsRightAndNegativeTurnsLeft()
        {
            yield return LoadGame();
            float right = 0f;
            yield return Walk(+1f, 3f, h => right = h);
            Debug.Log($"[Steer] +1 for 3 s turned {right:0.0} degrees; pelvis y {m_Game.Player.Pelvis.position.y:0.00}");

            yield return LoadGame();
            float left = 0f;
            yield return Walk(-1f, 3f, h => left = h);
            Debug.Log($"[Steer] -1 for 3 s turned {left:0.0} degrees; pelvis y {m_Game.Player.Pelvis.position.y:0.00}");

            Assert.Greater(right, 4f, "positive steer should turn right (positive heading); the even stride has a slight natural left bias");
            Assert.Less(left, -10f, "negative steer should turn left");
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator EvenStrideStaysRoughlyStraight()
        {
            yield return LoadGame();
            float drift = 0f;
            yield return Walk(0f, 4f, h => drift = h);
            Debug.Log($"[Steer] even stride drifted {drift:0.0} degrees");
            Assert.Less(Mathf.Abs(drift), 45f, "an even stride should not spin the doll around");
        }
    }
}
