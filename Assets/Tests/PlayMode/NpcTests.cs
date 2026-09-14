using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RedLightQwop.Tests
{
    public class NpcTests
    {
        GameManager m_Game;

        IEnumerator LoadGame()
        {
            yield return SceneManager.LoadSceneAsync("Game");
            yield return null;
            m_Game = Object.FindAnyObjectByType<GameManager>();
            m_Game.Controller.UseKeyboard = false;
            m_Game.ForcePhase(LightPhase.Green);
            yield return null;
        }

        static void MakeAlert(NpcBrain npc) { npc.ReactionMin = 0.05f; npc.ReactionMax = 0.05f; npc.LapseChance = 0f; }
        static void MakeSlow(NpcBrain npc) { npc.ReactionMin = 2.5f; npc.ReactionMax = 2.5f; npc.LapseChance = 0f; }

        [UnityTest, Timeout(60000)]
        public IEnumerator RunnersExistAndAdvanceOnGreen()
        {
            yield return LoadGame();
            Assert.GreaterOrEqual(m_Game.NpcCount, 6, "expected a crowd of runners");

            float startAvg = 0f;
            foreach (var npc in m_Game.Npcs) startAvg += npc.Ragdoll.Position.z;
            startAvg /= m_Game.NpcCount;

            yield return new WaitForSeconds(4f);

            float endAvg = 0f;
            int upright = 0;
            foreach (var npc in m_Game.Npcs)
            {
                endAvg += npc.Ragdoll.Position.z;
                if (npc.Ragdoll.Pelvis.position.y > 0.7f) upright++;
            }
            endAvg /= m_Game.NpcCount;

            Assert.Greater(endAvg - startAvg, 2f, "runners should advance on green");
            Assert.GreaterOrEqual(upright, m_Game.NpcCount - 2, "most runners should stay on their feet");
            Assert.AreEqual(GameState.Playing, m_Game.State, "NPC movement must not affect the player's state");
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator SlowRunnerIsEliminatedAndCollapsesOnRed()
        {
            yield return LoadGame();
            foreach (var npc in m_Game.Npcs) MakeAlert(npc);
            var slow = m_Game.Npcs[0];
            MakeSlow(slow);

            yield return new WaitForSeconds(2.5f);
            m_Game.ForcePhase(LightPhase.Red);
            yield return new WaitForSeconds(m_Game.RedGracePeriod + 1.0f);

            Assert.AreEqual(NpcBrain.NpcState.Eliminated, slow.State, "a runner still moving after the grace period is eliminated");
            Assert.IsFalse(slow.Ragdoll.BalanceAssist);

            yield return new WaitForSeconds(2f);
            Assert.Less(slow.Ragdoll.Pelvis.position.y, 0.55f, "an eliminated runner should have fallen over");
            Assert.AreEqual(GameState.Playing, m_Game.State, "the player is unaffected");
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator OneDollCanShoveAnother()
        {
            yield return LoadGame();
            Assert.IsTrue(m_Game.CharactersCollide);
            // Park every runner so only physics moves them.
            foreach (var npc in m_Game.Npcs) npc.StartDelay = 1000f;
            var pusher = m_Game.Npcs[0];
            var target = m_Game.Npcs[1];
            yield return new WaitForSeconds(1f);

            // Line the pusher up half a metre behind the target and fire it forward.
            Vector3 delta = target.Ragdoll.Position - pusher.Ragdoll.Position + new Vector3(0f, 0f, -0.6f);
            delta.y = 0f;
            pusher.Ragdoll.Teleport(delta);
            pusher.Ragdoll.AllowBraking = false;
            pusher.Ragdoll.SetBraking(false);
            yield return new WaitForFixedUpdate();
            Vector3 targetStart = target.Ragdoll.Position;
            pusher.Ragdoll.SetVelocity(Vector3.forward * 5f);
            yield return new WaitForSeconds(1.5f);

            float moved = target.Ragdoll.Position.z - targetStart.z;
            Assert.Greater(moved, 0.15f, "the struck doll should be pushed forward");
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator AlertRunnersSurviveRed()
        {
            yield return LoadGame();
            foreach (var npc in m_Game.Npcs) MakeAlert(npc);

            yield return new WaitForSeconds(2.5f);
            m_Game.ForcePhase(LightPhase.Red);
            var log = new System.Text.StringBuilder("[NpcRed] t | name | speed | pelvisY | state\n");
            float t = 0f;
            float[] checkpoints = { 0.4f, 0.8f, 1.2f, 2.0f };
            int next = 0;
            while (t < 3f)
            {
                yield return null;
                t += Time.deltaTime;
                if (next < checkpoints.Length && t >= checkpoints[next])
                {
                    next++;
                    foreach (var npc in m_Game.Npcs)
                        log.AppendLine($"[NpcRed] {t:0.00} | {npc.name} | {npc.Ragdoll.Speed:0.00} | {npc.Ragdoll.Pelvis.position.y:0.00} | {npc.State}");
                }
            }
            Debug.Log(log.ToString());

            int eliminated = 0;
            foreach (var npc in m_Game.Npcs) if (npc.State == NpcBrain.NpcState.Eliminated) eliminated++;
            Assert.LessOrEqual(eliminated, 1, "alert runners should almost all stop in time");
        }
    }
}
