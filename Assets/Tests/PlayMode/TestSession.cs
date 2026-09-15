using System.Collections;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RedLightQwop.Tests
{
    /// <summary>Shared helpers: load the Game scene, start a solo host, wait for spawns, tear down.</summary>
    public static class TestSession
    {
        static ushort s_NextPort = 7900;

        public static IEnumerator Load(System.Action<GameManager> onReady, bool parkRunners = false)
        {
            yield return Teardown();
            yield return SceneManager.LoadSceneAsync("Game");
            yield return null;

            var game = Object.FindAnyObjectByType<GameManager>();
            Assert.IsNotNull(game, "GameManager missing from the Game scene");
            game.Port = s_NextPort++;
            Assert.IsTrue(game.StartSolo(), "solo host should start");

            float waited = 0f;
            while ((game.Net == null || game.Player == null || game.NpcCount < game.NpcSpawns.Length) && waited < 10f)
            {
                waited += Time.deltaTime;
                yield return null;
            }
            Assert.IsNotNull(game.Net, "game state should spawn");
            Assert.IsNotNull(game.Player, "local player should spawn");
            Assert.AreEqual(game.NpcSpawns.Length, game.NpcCount, "all runners should spawn");
            Assert.IsTrue(game.RoundRunning);

            if (parkRunners) foreach (var npc in game.Npcs) npc.StartDelay = 1000f;
            game.ForcePhase(LightPhase.Green);
            yield return new WaitForFixedUpdate();
            onReady(game);
        }

        public static IEnumerator Teardown()
        {
            var nm = NetworkManager.Singleton;
            if (nm != null)
            {
                if (nm.IsListening) nm.Shutdown();
                float waited = 0f;
                while ((nm.IsListening || nm.ShutdownInProgress) && waited < 5f) { waited += Time.deltaTime; yield return null; }
                yield return null;
            }
        }
    }
}

