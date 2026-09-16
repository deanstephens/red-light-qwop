using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace RedLightQwop.Tests
{
    public class DifficultyTests
    {
        GameManager m_Game;

        [UnityTearDown]
        public IEnumerator TearDown() => TestSession.Teardown();

        [UnityTest, Timeout(60000)]
        public IEnumerator HostDifficultyScalesEveryDoll()
        {
            yield return TestSession.Load(g => m_Game = g, parkRunners: true);
            var hip = m_Game.Player.LeftHip;
            float normalSpring = hip.Spring;
            Assert.AreEqual(Difficulty.Normal, m_Game.CurrentDifficulty);
            Assert.AreEqual(hip.OriginalSpring, normalSpring, 0.01f);

            m_Game.SetDifficulty(Difficulty.Hard);
            yield return null;

            var hard = m_Game.DifficultyStrength[(int)Difficulty.Hard];
            Assert.AreEqual(Difficulty.Hard, m_Game.CurrentDifficulty);
            Assert.AreEqual((int)Difficulty.Hard, m_Game.Net.Difficulty.Value, "difficulty is replicated through the game state");
            Assert.AreEqual(hip.OriginalSpring * hard.x, hip.Spring, 0.01f, "player muscles follow the host difficulty");
            Assert.AreEqual(hip.OriginalSpring * hard.x, hip.Joint.slerpDrive.positionSpring, 0.01f, "the joint drive itself is updated");
            foreach (var npc in m_Game.Npcs)
            {
                Assert.AreEqual(hard.x, npc.Ragdoll.MuscleScale, 0.001f, $"{npc.name} muscles follow the host difficulty");
                Assert.AreEqual(hard.y, npc.Ragdoll.BalanceScale, 0.001f, $"{npc.name} balance follows the host difficulty");
            }

            // A restart keeps the chosen difficulty on the respawned dolls.
            m_Game.RestartRound();
            yield return null;
            yield return null;
            Assert.AreEqual(hard.x, m_Game.Player.MuscleScale, 0.001f, "player keeps difficulty through a restart");
            Assert.AreEqual(hard.x, m_Game.Npcs[0].Ragdoll.MuscleScale, 0.001f, "respawned runners get the current difficulty");
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator BracketCyclingWrapsAround()
        {
            yield return TestSession.Load(g => m_Game = g, parkRunners: true);
            m_Game.CycleDifficulty(-1);
            Assert.AreEqual(Difficulty.Easy, m_Game.CurrentDifficulty);
            m_Game.CycleDifficulty(-1);
            Assert.AreEqual(Difficulty.Brutal, m_Game.CurrentDifficulty, "cycling below Easy wraps to Brutal");
            m_Game.CycleDifficulty(+1);
            Assert.AreEqual(Difficulty.Easy, m_Game.CurrentDifficulty);
            yield return null;
        }
    }
}
