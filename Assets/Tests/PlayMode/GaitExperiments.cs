using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace RedLightQwop.Tests
{
    /// <summary>
    /// Scripted gait patterns, run for a fixed time and measured. Not a pass/fail test: it logs
    /// distance travelled and whether the doll fell, to guide tuning and player advice.
    /// </summary>
    public class GaitExperiments
    {
        struct Step { public float L, R, LK, RK, T; }

        static Step S(float l, float r, float lk, float rk, float t) => new Step { L = l, R = r, LK = lk, RK = rk, T = t };

        static readonly Dictionary<string, Step[]> k_Gaits = new Dictionary<string, Step[]>
        {
            // Q/W only, alternating.
            ["HipsOnly_0.5s"] = new[] { S(1, -1, 0, 0, 0.5f), S(-1, 1, 0, 0, 0.5f) },
            ["HipsOnly_0.3s"] = new[] { S(1, -1, 0, 0, 0.3f), S(-1, 1, 0, 0, 0.3f) },
            // Full cycle: lift (hip + knee), plant (hip only), then the other side.
            ["LiftPlant_0.3s"] = new[] { S(1, -1, 1, -1, 0.3f), S(1, -1, -1, 1, 0.3f), S(-1, 1, -1, 1, 0.3f), S(-1, 1, 1, -1, 0.3f) },
            ["LiftPlant_0.2s"] = new[] { S(1, -1, 1, -1, 0.2f), S(1, -1, -1, 1, 0.2f), S(-1, 1, -1, 1, 0.2f), S(-1, 1, 1, -1, 0.2f) },
            ["LiftPlant_0.45s"] = new[] { S(1, -1, 1, -1, 0.45f), S(1, -1, -1, 1, 0.45f), S(-1, 1, -1, 1, 0.45f), S(-1, 1, 1, -1, 0.45f) },
            // Knee first, then hip (O, Q, P, W).
            ["KneeThenHip_0.3s"] = new[] { S(0, 0, 1, -1, 0.3f), S(1, -1, 0, 0, 0.3f), S(0, 0, -1, 1, 0.3f), S(-1, 1, 0, 0, 0.3f) },
            // Small shuffle: half-strength hips.
            ["HalfHips_0.4s"] = new[] { S(0.5f, -0.5f, 0, 0, 0.4f), S(-0.5f, 0.5f, 0, 0, 0.4f) },
        };

        [UnityTest, Timeout(300000)]
        public IEnumerator MeasureGaits()
        {
            var report = new System.Text.StringBuilder("[Gait] name | distance m | min pelvis y | fell | settle time after release | min y after\n");
            foreach (var kv in k_Gaits)
            {
                yield return SceneManager.LoadSceneAsync("Game");
                yield return null;
                var game = Object.FindAnyObjectByType<GameManager>();
                var rag = game.Player;
                var ctl = game.Controller;
                ctl.UseKeyboard = false;
                game.ForcePhase(LightPhase.Green);
                yield return new WaitForSeconds(1f);

                float startZ = rag.Position.z;
                float minY = float.MaxValue;
                float elapsed = 0f;
                int idx = 0;
                float stepT = 0f;
                var steps = kv.Value;
                ctl.Current = new LimbInput { LeftHip = steps[0].L, RightHip = steps[0].R, LeftKnee = steps[0].LK, RightKnee = steps[0].RK };
                while (elapsed < 8f)
                {
                    yield return null;
                    float dt = Time.deltaTime;
                    elapsed += dt;
                    stepT += dt;
                    if (stepT >= steps[idx].T)
                    {
                        stepT = 0f;
                        idx = (idx + 1) % steps.Length;
                        ctl.Current = new LimbInput { LeftHip = steps[idx].L, RightHip = steps[idx].R, LeftKnee = steps[idx].LK, RightKnee = steps[idx].RK };
                    }
                    minY = Mathf.Min(minY, rag.Pelvis.position.y);
                }
                float dist = rag.Position.z - startZ;

                // Release everything and measure how long the doll keeps moving (matters for red light).
                ctl.Current = default;
                float settleT = -1f, t = 0f, minYAfter = float.MaxValue;
                while (t < 2.5f)
                {
                    yield return null;
                    t += Time.deltaTime;
                    minYAfter = Mathf.Min(minYAfter, rag.Pelvis.position.y);
                    if (settleT < 0f && rag.Speed < game.MoveThreshold) settleT = t;
                    else if (settleT >= 0f && rag.Speed >= game.MoveThreshold) settleT = -1f; // moved again, keep waiting
                }
                report.AppendLine($"[Gait] {kv.Key} | {dist:0.00} | {minY:0.00} | {(minY < 0.55f ? "FELL" : "ok")} | settle {settleT:0.00}s | minY after stop {minYAfter:0.00}");
            }
            Debug.Log(report.ToString());
        }
    }
}
