using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RedLightQwop.Tests
{
    /// <summary>
    /// PlayMode tests that load the built Game scene and exercise the core mechanics with the
    /// keyboard disabled, so they run headless.
    /// </summary>
    public class GameFlowTests
    {
        GameManager m_Game;
        Ragdoll m_Ragdoll;
        LimbController m_Controller;

        IEnumerator LoadGame()
        {
            yield return TestSession.Load(g => { m_Game = g; m_Ragdoll = g.Player; m_Controller = g.Controller; }, parkRunners: true);
            m_Controller.Current = default;
            m_Game.LocalPlayer.enabled = false; // tests drive Controller.Current directly
        }

        [UnityTearDown]
        public IEnumerator TearDown() => TestSession.Teardown();

        float LeftFootForwardOffset => m_Ragdoll.LeftLowerLeg.position.z - m_Ragdoll.Pelvis.position.z;

        [UnityTest, Timeout(30000)]
        public IEnumerator RagdollStandsWithoutInput()
        {
            yield return LoadGame();
            yield return new WaitForSeconds(3f);

            Assert.Greater(m_Ragdoll.Pelvis.position.y, 0.75f, "pelvis should still be roughly at standing height");
            Assert.Less(m_Ragdoll.Speed, 0.3f, "ragdoll should be settled");
        }

        [UnityTest, Timeout(30000)]
        public IEnumerator LeftHipForwardSwingsLeftFootForward()
        {
            yield return LoadGame();
            yield return new WaitForSeconds(1f);
            float before = LeftFootForwardOffset;

            m_Controller.Current = new LimbInput { LeftHip = 1f, RightHip = -1f };
            yield return new WaitForSeconds(1f);

            Assert.Greater(LeftFootForwardOffset, before + 0.15f, "left foot should move ahead of the pelvis");
        }

        [UnityTest, Timeout(30000)]
        public IEnumerator LeftKneeBendLiftsLeftFoot()
        {
            yield return LoadGame();
            yield return new WaitForSeconds(1f);
            float before = m_Ragdoll.LeftLowerLeg.position.y;

            m_Controller.Current = new LimbInput { LeftKnee = 1f, RightKnee = -1f };
            yield return new WaitForSeconds(1f);

            Assert.Greater(m_Ragdoll.LeftLowerLeg.position.y, before + 0.08f, "bending the knee should lift the lower leg");
        }

        [UnityTest, Timeout(30000)]
        public IEnumerator StandingStillOnRedIsSafe()
        {
            yield return LoadGame();
            yield return new WaitForSeconds(1f);

            m_Game.ForcePhase(LightPhase.Red);
            yield return new WaitForSeconds(3f);

            Assert.AreEqual(GameState.Playing, m_Game.State);
        }

        [UnityTest, Timeout(30000)]
        public IEnumerator MovingOnRedEliminates()
        {
            yield return LoadGame();
            yield return new WaitForSeconds(1f);

            m_Game.ForcePhase(LightPhase.Red);
            yield return new WaitForSeconds(m_Game.RedGracePeriod + 0.1f);
            m_Ragdoll.SetVelocity(Vector3.forward * 2f);
            yield return new WaitForSeconds(0.6f);

            Assert.AreEqual(GameState.Eliminated, m_Game.State);
            Assert.IsFalse(m_Controller.InputEnabled, "input should lock after elimination");
            Assert.IsFalse(m_Ragdoll.BalanceAssist, "an eliminated player goes limp");
        }

        [UnityTest, Timeout(30000)]
        public IEnumerator CrossingFinishLineWins()
        {
            yield return LoadGame();
            yield return new WaitForSeconds(0.5f);

            float startZ = m_Ragdoll.Position.z;
            // Idle braking slows a doll with no input held, so start close and push firmly.
            float targetZ = m_Game.FinishLine.position.z - 0.5f;
            m_Ragdoll.Teleport(new Vector3(0f, 0f, targetZ - startZ));
            yield return new WaitForFixedUpdate();
            m_Ragdoll.SetVelocity(Vector3.forward * 4f);
            yield return new WaitForSeconds(1.5f);

            Assert.AreEqual(GameState.Won, m_Game.State);
        }
    }
}
