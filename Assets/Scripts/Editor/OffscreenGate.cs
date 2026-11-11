using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Crusher.Config;
using Crusher.Core;
using Crusher.Sim;
using Crusher.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace Crusher.Editor
{
    /// <summary>
    /// Headless URP 17 offscreen showcase renderer & pixel QA gate (STOP-GATE 2).
    /// Uses Smith-Blinn Dual-Background Exact Affine Compositing (rtWorld + rtUiBlack + rtUiWhite)
    /// so URP 17 RenderGraph never discards the 3D ACES+Bloom buffer and uGUI renders with 100% crisp sRGB fidelity.
    /// </summary>
    public static class OffscreenGate
    {
        public const int WIDTH = 1080;
        public const int HEIGHT = 2400;
        private const int UI_LAYER = 5; // Built-in "UI" layer

        private struct FrameStats
        {
            public string FileName;
            public long Bytes;
            public float WorldMeanLuma;
            public float WorldStdLuma;
            public float FinalMeanLuma;
            public float FinalStdLuma;
            public float MagentaRatio;
            public bool Passed;
        }

        [MenuItem("Crusher/QA/Run Offscreen Graphics Gate 2 (5x 1080x2400)")]
        public static void Run()
        {
            Debug.Log("[OffscreenGate] Opening Assets/Scenes/Main.unity for STOP-GATE 2 offscreen capture...");
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                throw new InvalidOperationException(
                    "[OffscreenGate] Graphics device is Null! Invoke unity_batch.ps1 with -KeepGraphics.");
            }

            EditorSceneManager.OpenScene(SceneBuilder.MainScenePath, OpenSceneMode.Single);

            Bootstrap bootstrap = UnityEngine.Object.FindAnyObjectByType<Bootstrap>();
            if (bootstrap == null)
            {
                throw new InvalidOperationException("[OffscreenGate] Bootstrap not found in Assets/Scenes/Main.unity.");
            }

            string qaDir = Path.Combine(Directory.GetCurrentDirectory(), "build", "qa");
            if (!Directory.Exists(qaDir))
            {
                Directory.CreateDirectory(qaDir);
            }

            RenderTexture rtWorld = CreateSrgbRt("OffscreenGate_WorldRT");
            RenderTexture rtUiBlack = CreateSrgbRt("OffscreenGate_UiBlackRT");
            RenderTexture rtUiWhite = CreateSrgbRt("OffscreenGate_UiWhiteRT");

            var texWorld = new Texture2D(WIDTH, HEIGHT, TextureFormat.RGBA32, mipChain: false, linear: false);
            var texUiBlack = new Texture2D(WIDTH, HEIGHT, TextureFormat.RGBA32, mipChain: false, linear: false);
            var texUiWhite = new Texture2D(WIDTH, HEIGHT, TextureFormat.RGBA32, mipChain: false, linear: false);
            var texComposite = new Texture2D(WIDTH, HEIGHT, TextureFormat.RGBA32, mipChain: false, linear: false);

            var uiCamGo = new GameObject("OffscreenUiCamera_Temp");
            uiCamGo.transform.position = new Vector3(0f, 0f, -10f);
            uiCamGo.transform.rotation = Quaternion.identity;
            var uiCam = uiCamGo.AddComponent<Camera>();
            uiCam.orthographic = true;
            uiCam.orthographicSize = 5f;
            uiCam.nearClipPlane = 0.1f;
            uiCam.farClipPlane = 20f;
            uiCam.clearFlags = CameraClearFlags.SolidColor;
            uiCam.backgroundColor = new Color(0f, 0f, 0f, 1f);
            uiCam.cullingMask = 1 << UI_LAYER;
            uiCam.allowHDR = false;
            uiCam.allowMSAA = false;
            uiCam.targetTexture = rtUiBlack;

            var uiCamData = uiCamGo.AddComponent<UniversalAdditionalCameraData>();
            uiCamData.renderPostProcessing = false;
            uiCamData.antialiasing = AntialiasingMode.None;

            Camera worldCam = bootstrap.CameraRig != null ? bootstrap.CameraRig.WorldCamera : Camera.main;
            if (worldCam == null)
            {
                throw new InvalidOperationException("[OffscreenGate] World Camera missing on CameraRig.");
            }

            int origWorldMask = worldCam.cullingMask;
            RenderTexture origWorldRt = worldCam.targetTexture;
            var worldCamData = worldCam.GetComponent<UniversalAdditionalCameraData>();
            AntialiasingMode origAa = worldCamData != null ? worldCamData.antialiasing : AntialiasingMode.None;

            worldCam.cullingMask = origWorldMask & ~(1 << UI_LAYER);
            worldCam.targetTexture = rtWorld;
            if (worldCamData != null)
            {
                worldCamData.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            }

            var results = new List<FrameStats>(5);
            try
            {
                // Scenario 1: Run 1 Wave 1 Rusty Core + Basic cubes + Bottom Combat Upgrade Deck
                GameSession s1 = BuildScenario01_Wave1Rusty();
                results.Add(CaptureScenario(bootstrap, s1, worldCam, uiCam,
                    rtWorld, rtUiBlack, rtUiWhite, texWorld, texUiBlack, texUiWhite, texComposite,
                    qaDir, "gate2_01_w1_rusty.png", warmupSteps: 55, stepDt: 0.08f));

                // Scenario 2: Wave 10 Boss ATM + G1 Steel Carbide + Sparks/Debris/Damage Popups
                GameSession s2 = BuildScenario02_Wave10BossAtm();
                results.Add(CaptureScenario(bootstrap, s2, worldCam, uiCam,
                    rtWorld, rtUiBlack, rtUiWhite, texWorld, texUiBlack, texUiWhite, texComposite,
                    qaDir, "gate2_02_w10_boss_atm.png", warmupSteps: 36, stepDt: 0.05f, postWarmupHook: sim =>
                    {
                        EnsureShowcaseBossAndAction(sim);
                    }));

                // Scenario 3: Wave 45 G2 Titanium Blender + Plasma Saws + Death Ray + Vampire Siphon (+ Garlic rings)
                GameSession s3 = BuildScenario03_Wave45BlenderDeathRay();
                results.Add(CaptureScenario(bootstrap, s3, worldCam, uiCam,
                    rtWorld, rtUiBlack, rtUiWhite, texWorld, texUiBlack, texUiWhite, texComposite,
                    qaDir, "gate2_03_w45_blender_deathray.png", warmupSteps: 34, stepDt: 0.05f, postWarmupHook: sim =>
                    {
                        EnsureShowcaseDeathRayAndVampire(sim);
                    }));

                // Scenario 4: Wave 85 G3 Diamond Reactor + Molten Core + Synced GT + BH Vortex + Chain Lightning + Golden Bot + Gem Drone
                GameSession s4 = BuildScenario04_Wave85GtBhMolten();
                results.Add(CaptureScenario(bootstrap, s4, worldCam, uiCam,
                    rtWorld, rtUiBlack, rtUiWhite, texWorld, texUiBlack, texUiWhite, texComposite,
                    qaDir, "gate2_04_w85_gt_bh_moltencore.png", warmupSteps: 38, stepDt: 0.05f, postWarmupHook: sim =>
                    {
                        EnsureShowcaseSyncedOverdrive(sim);
                    }));

                // Scenario 5: Hangar Meta Progression Screen (Workshop / Cards / Labs / UWs)
                GameSession s5 = BuildScenario05_HangarMeta();
                results.Add(CaptureScenario(bootstrap, s5, worldCam, uiCam,
                    rtWorld, rtUiBlack, rtUiWhite, texWorld, texUiBlack, texUiWhite, texComposite,
                    qaDir, "gate2_05_hangar_meta.png", warmupSteps: 6, stepDt: 0.04f));

                WriteGateReport(qaDir, results);

                foreach (FrameStats fs in results)
                {
                    if (!fs.Passed)
                    {
                        throw new InvalidOperationException(
                            $"[OffscreenGate] Frame QA FAILED on {fs.FileName}: worldLuma={fs.WorldMeanLuma:F3} (std={fs.WorldStdLuma:F3}), finalLuma={fs.FinalMeanLuma:F3}, magenta={fs.MagentaRatio:P4}");
                    }
                }

                Debug.Log("[OffscreenGate] STOP_GATE_2_RENDER_PASS — All 5 1080x2400 showcase frames rendered & verified cleanly.");
            }
            finally
            {
                worldCam.cullingMask = origWorldMask;
                worldCam.targetTexture = origWorldRt;
                if (worldCamData != null) worldCamData.antialiasing = origAa;
                uiCam.targetTexture = null;
                RenderTexture.active = null;
                UnityEngine.Object.DestroyImmediate(uiCamGo);
                UnityEngine.Object.DestroyImmediate(texWorld);
                UnityEngine.Object.DestroyImmediate(texUiBlack);
                UnityEngine.Object.DestroyImmediate(texUiWhite);
                UnityEngine.Object.DestroyImmediate(texComposite);
                ReleaseRt(rtWorld);
                ReleaseRt(rtUiBlack);
                ReleaseRt(rtUiWhite);
            }
        }

        private static RenderTexture CreateSrgbRt(string name)
        {
            var rt = new RenderTexture(WIDTH, HEIGHT, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                name = name,
                antiAliasing = 1,
                filterMode = FilterMode.Bilinear
            };
            rt.Create();
            return rt;
        }

        private static void ReleaseRt(RenderTexture rt)
        {
            if (rt == null) return;
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
        }

        private static FrameStats CaptureScenario(
            Bootstrap bootstrap,
            GameSession session,
            Camera worldCam,
            Camera uiCam,
            RenderTexture rtWorld,
            RenderTexture rtUiBlack,
            RenderTexture rtUiWhite,
            Texture2D texWorld,
            Texture2D texUiBlack,
            Texture2D texUiWhite,
            Texture2D texComposite,
            string qaDir,
            string fileName,
            int warmupSteps,
            float stepDt,
            Action<CombatSim> postWarmupHook = null)
        {
            bootstrap.InitializeRuntimeSystems(session);

            CrusherUiRoot uiRoot = bootstrap.UiRoot;
            if (uiRoot != null)
            {
                uiCam.targetTexture = rtUiBlack;
                uiRoot.SetOffscreenTargetCamera(uiCam);
                if (uiRoot.MainCanvas != null)
                {
                    SetLayerRecursive(uiRoot.MainCanvas.gameObject, UI_LAYER);
                }
            }

            for (int i = 0; i < warmupSteps; i++)
            {
                bootstrap.StepFrame(stepDt, tickSimulation: session.InRun);
            }

            if (postWarmupHook != null && session.InRun)
            {
                postWarmupHook(session.Combat);
                // First render-only step consumes KillEvents -> spawns Voronoi chunks, welding sparks & gold ingots
                bootstrap.StepFrame(0.020f, tickSimulation: false);
                // Advance ballistic VFX & shader blends without expiring short-lived showcase ChainArcs / Boss poses
                for (int k = 0; k < 5; k++)
                {
                    bootstrap.StepFrame(0.028f, tickSimulation: false);
                }
            }
            else
            {
                bootstrap.StepFrame(0.033f, tickSimulation: false);
            }

            if (uiRoot != null && uiRoot.MainCanvas != null)
            {
                SetLayerRecursive(uiRoot.MainCanvas.gameObject, UI_LAYER);
                Canvas.ForceUpdateCanvases();
                if (uiRoot.MainCanvas.transform is RectTransform rtCanvas)
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate(rtCanvas);
                }
                Canvas.ForceUpdateCanvases();
            }

            // Pass 1: 3D URP World + Shadows + GPU Instancing + ACES Tonemapping + HDR Bloom
            worldCam.targetTexture = rtWorld;
            bootstrap.SubmitAllInstancedDraws(worldCam);
            worldCam.Render();
            ReadRtIntoTexture(rtWorld, texWorld);

            // Pass 2A & 2B: UI over solid Black & solid White for exact affine alpha extraction
            uiCam.clearFlags = CameraClearFlags.SolidColor;
            uiCam.backgroundColor = new Color(0f, 0f, 0f, 1f);
            uiCam.targetTexture = rtUiBlack;
            uiCam.Render();
            ReadRtIntoTexture(rtUiBlack, texUiBlack);

            uiCam.backgroundColor = new Color(1f, 1f, 1f, 1f);
            uiCam.targetTexture = rtUiWhite;
            uiCam.Render();
            ReadRtIntoTexture(rtUiWhite, texUiWhite);

            Color32[] pWorld = texWorld.GetPixels32();
            Color32[] pBlack = texUiBlack.GetPixels32();
            Color32[] pWhite = texUiWhite.GetPixels32();
            var pOut = new Color32[pWorld.Length];

            const float inv255 = 1.0f / 255.0f;
            double sumWorldLuma = 0.0;
            double sumWorldSq = 0.0;
            double sumFinalLuma = 0.0;
            double sumFinalSq = 0.0;
            int magentaCount = 0;
            int sampled = 0;

            for (int i = 0; i < pWorld.Length; i++)
            {
                float wr = pWorld[i].r * inv255;
                float wg = pWorld[i].g * inv255;
                float wb = pWorld[i].b * inv255;

                float br = pBlack[i].r * inv255;
                float bg = pBlack[i].g * inv255;
                float bb = pBlack[i].b * inv255;

                float whr = pWhite[i].r * inv255;
                float whg = pWhite[i].g * inv255;
                float whb = pWhite[i].b * inv255;

                // Exact transmittance T = F(1) - F(0); composite = F(0) + C_world * T
                float tr = Mathf.Clamp01(whr - br);
                float tg = Mathf.Clamp01(whg - bg);
                float tb = Mathf.Clamp01(whb - bb);

                float fr = Mathf.Clamp01(br + wr * tr);
                float fg = Mathf.Clamp01(bg + wg * tg);
                float fb = Mathf.Clamp01(bb + wb * tb);

                pOut[i] = new Color32(
                    (byte)Mathf.RoundToInt(fr * 255f),
                    (byte)Mathf.RoundToInt(fg * 255f),
                    (byte)Mathf.RoundToInt(fb * 255f),
                    255);

                if ((i & 3) == 0)
                {
                    float wLuma = 0.2126f * wr + 0.7152f * wg + 0.0722f * wb;
                    float fLuma = 0.2126f * fr + 0.7152f * fg + 0.0722f * fb;
                    sumWorldLuma += wLuma;
                    sumWorldSq += wLuma * wLuma;
                    sumFinalLuma += fLuma;
                    sumFinalSq += fLuma * fLuma;
                    sampled++;

                    if ((wr > 0.94f && wg < 0.05f && wb > 0.94f) ||
                        (fr > 0.94f && fg < 0.05f && fb > 0.94f))
                    {
                        magentaCount++;
                    }
                }
            }

            texComposite.SetPixels32(pOut);
            texComposite.Apply(false, false);

            byte[] pngBytes = texComposite.EncodeToPNG();
            string outPath = Path.Combine(qaDir, fileName);
            File.WriteAllBytes(outPath, pngBytes);

            float wMean = (float)(sumWorldLuma / Math.Max(1, sampled));
            float wStd = Mathf.Sqrt(Mathf.Max(0f, (float)(sumWorldSq / Math.Max(1, sampled)) - wMean * wMean));
            float fMean = (float)(sumFinalLuma / Math.Max(1, sampled));
            float fStd = Mathf.Sqrt(Mathf.Max(0f, (float)(sumFinalSq / Math.Max(1, sampled)) - fMean * fMean));
            float magRatio = (float)magentaCount / Math.Max(1, sampled);

            bool worldValid = wMean > 0.25f && wMean < 0.90f && wStd > 0.035f;
            bool finalValid = pngBytes.Length > 150 * 1024 && fMean > 0.12f && fMean < 0.90f && fStd > 0.08f && magRatio < 0.0005f;
            bool pass = worldValid && finalValid;

            Debug.Log(
                $"[OffscreenGate] CAPTURED {fileName} | Size={pngBytes.Length / 1024} KB | WorldLuma={wMean:F3}(std={wStd:F3}) | FinalLuma={fMean:F3}(std={fStd:F3}) | Magenta={magRatio:P4} | Pass={pass}");

            return new FrameStats
            {
                FileName = fileName,
                Bytes = pngBytes.Length,
                WorldMeanLuma = wMean,
                WorldStdLuma = wStd,
                FinalMeanLuma = fMean,
                FinalStdLuma = fStd,
                MagentaRatio = magRatio,
                Passed = pass
            };
        }

        private static void ReadRtIntoTexture(RenderTexture rt, Texture2D dst)
        {
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            dst.ReadPixels(new Rect(0, 0, WIDTH, HEIGHT), 0, 0, recalculateMipMaps: false);
            dst.Apply(false, false);
            RenderTexture.active = prev;
        }

        private static void SetLayerRecursive(GameObject root, int layer)
        {
            if (root == null) return;
            Transform[] trs = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < trs.Length; i++)
            {
                trs[i].gameObject.layer = layer;
            }
        }

        private static void MarkMilestonesUpToWave(GameSession s, int maxClaimedWave)
        {
            s.BestWaveT1 = Math.Max(s.BestWaveT1, maxClaimedWave);
            for (int i = 0; i < BalanceTables.Tier1Milestones.Length && i < s.ClaimedMilestonesT1.Length; i++)
            {
                if (BalanceTables.Tier1Milestones[i].Wave <= maxClaimedWave)
                {
                    s.ClaimedMilestonesT1[i] = true;
                }
            }
            s.ToastCount = 0;
        }

        // --- Deterministic Showcase Session Builders ---

        private static GameSession BuildScenario01_Wave1Rusty()
        {
            var s = new GameSession(10101UL);
            s.StartNewRun(1);
            s.RunCash = 58.0;
            return s;
        }

        private static GameSession BuildScenario02_Wave10BossAtm()
        {
            var s = new GameSession(20202UL);
            s.TotalRunsStarted = 2;
            s.TotalRunsCompleted = 2;
            s.Speed15Unlocked = true;
            s.WorkshopUnlocked = true;
            s.FtueDefenseArrowCompleted = true;
            s.SelectedGameSpeed = 2.0f;
            s.Coins = 145;
            s.Gems = 35;
            MarkMilestonesUpToWave(s, 5); // W10 milestone will pop authentically on W10!

            s.UnlockGroups[(int)UnlockGroup.DefenseAbs] = true;
            s.UnlockGroups[(int)UnlockGroup.DefensePct] = true;
            s.UnlockGroups[(int)UnlockGroup.Thorns] = true;
            s.UnlockGroups[(int)UnlockGroup.Crit] = true;
            s.UnlockGroups[(int)UnlockGroup.Coins] = true;

            s.WorkshopLevels[(int)StatId.Damage] = 8;
            s.WorkshopLevels[(int)StatId.AttackSpeed] = 6;
            s.WorkshopLevels[(int)StatId.Health] = 10;
            s.WorkshopLevels[(int)StatId.DefenseAbsolute] = 8;
            s.WorkshopLevels[(int)StatId.Thorns] = 12;
            s.WorkshopLevels[(int)StatId.CoinsPerKill] = 5;

            s.StartNewRun(1);
            s.Combat.CurrentWave = 10;
            s.Combat.WaveElapsed = 6.2f;
            s.RunCash = 340.0;
            return s;
        }

        private static GameSession BuildScenario03_Wave45BlenderDeathRay()
        {
            var s = new GameSession(30303UL);
            s.TotalRunsStarted = 7;
            s.TotalRunsCompleted = 7;
            s.Speed15Unlocked = true;
            s.WorkshopUnlocked = true;
            s.LabsUnlocked = true;
            s.CardsUnlocked = true;
            s.FtueDefenseArrowCompleted = true;
            s.SelectedGameSpeed = 3.5f;
            s.Coins = 2850;
            s.Gems = 95;
            s.EliteCells = 14;
            MarkMilestonesUpToWave(s, 45);

            for (int g = 0; g <= (int)UnlockGroup.FreeUp; g++)
                s.UnlockGroups[g] = true;

            s.WorkshopLevels[(int)StatId.Damage] = 28;
            s.WorkshopLevels[(int)StatId.AttackSpeed] = 22;
            s.WorkshopLevels[(int)StatId.Range] = 6;
            s.WorkshopLevels[(int)StatId.MultishotChance] = 18;
            s.WorkshopLevels[(int)StatId.MultishotTargets] = 2;
            s.WorkshopLevels[(int)StatId.Health] = 32;
            s.WorkshopLevels[(int)StatId.DefensePercent] = 18;
            s.WorkshopLevels[(int)StatId.Thorns] = 35;
            s.WorkshopLevels[(int)StatId.KnockbackChance] = 24;
            s.WorkshopLevels[(int)StatId.KnockbackForce] = 10;
            s.WorkshopLevels[(int)StatId.OrbsCount] = 4;
            s.WorkshopLevels[(int)StatId.OrbSpeed] = 12;

            s.LabLevels[(int)LabId.GameSpeed] = 2;
            s.LabLevels[(int)LabId.MagneticGarlic] = 3;
            s.LabLevels[(int)LabId.OrbAndRotorSpeed] = 2;

            s.UnlockedCardSlots = 3;
            s.CardCopies[(int)CardId.DeathRay] = 2;
            s.CardCopies[(int)CardId.ExtraOrb] = 2;
            s.CardCopies[(int)CardId.EnemyBalance] = 3;
            s.EquippedCards[0] = (int)CardId.DeathRay;
            s.EquippedCards[1] = (int)CardId.ExtraOrb;
            s.EquippedCards[2] = (int)CardId.EnemyBalance;

            s.StartNewRun(1);
            s.Combat.CurrentWave = 45;
            s.Combat.DeathRay.CycleTimer = 0.2f;
            s.RunCash = 4280.0;
            return s;
        }

        private static GameSession BuildScenario04_Wave85GtBhMolten()
        {
            var s = new GameSession(40404UL);
            s.TotalRunsStarted = 14;
            s.TotalRunsCompleted = 14;
            s.Speed15Unlocked = true;
            s.WorkshopUnlocked = true;
            s.LabsUnlocked = true;
            s.CardsUnlocked = true;
            s.UwSystemUnlocked = true;
            s.GoldenBotUnlocked = true;
            s.MoltenCoreSkinUnlocked = true;
            s.MoltenCoreSkinEquipped = true;
            s.FtueDefenseArrowCompleted = true;
            s.SelectedGameSpeed = 4.3f;

            s.Coins = 48600;
            s.Gems = 210;
            s.Stones = 45;
            s.EliteCells = 32;
            MarkMilestonesUpToWave(s, 85);

            for (int g = 0; g <= (int)UnlockGroup.FreeUp; g++)
                s.UnlockGroups[g] = true;

            s.WorkshopLevels[(int)StatId.Damage] = 55;
            s.WorkshopLevels[(int)StatId.AttackSpeed] = 42;
            s.WorkshopLevels[(int)StatId.CritChance] = 35;
            s.WorkshopLevels[(int)StatId.CritFactor] = 32;
            s.WorkshopLevels[(int)StatId.Range] = 8;
            s.WorkshopLevels[(int)StatId.MultishotChance] = 45;
            s.WorkshopLevels[(int)StatId.MultishotTargets] = 4;
            s.WorkshopLevels[(int)StatId.Health] = 65;
            s.WorkshopLevels[(int)StatId.DefensePercent] = 35;
            s.WorkshopLevels[(int)StatId.Thorns] = 55;
            s.WorkshopLevels[(int)StatId.OrbsCount] = 4;
            s.WorkshopLevels[(int)StatId.OrbSpeed] = 18;
            s.WorkshopLevels[(int)StatId.CoinsPerKill] = 28;

            s.LabLevels[(int)LabId.GameSpeed] = 3;
            s.LabLevels[(int)LabId.MagneticGarlic] = 4;
            s.LabLevels[(int)LabId.CoinsPerKill] = 4;
            s.LabLevels[(int)LabId.BlackHoleCoinBonus] = 3;

            s.UwUnlocked[(int)UwId.GoldenTower] = true;
            s.UwUnlocked[(int)UwId.BlackHole] = true;
            s.UwUnlocked[(int)UwId.ChainLightning] = true;
            s.UwUnlockedCount = 3;
            s.UwLevels[(int)UwId.GoldenTower] = 3;
            s.UwLevels[(int)UwId.BlackHole] = 3;
            s.UwLevels[(int)UwId.ChainLightning] = 3;

            s.UnlockedCardSlots = 4;
            s.CardCopies[(int)CardId.Coins] = 5;
            s.CardCopies[(int)CardId.EnemyBalance] = 5;
            s.CardCopies[(int)CardId.ExtraOrb] = 5;
            s.CardCopies[(int)CardId.PlasmaCannon] = 3;
            s.EquippedCards[0] = (int)CardId.Coins;
            s.EquippedCards[1] = (int)CardId.EnemyBalance;
            s.EquippedCards[2] = (int)CardId.ExtraOrb;
            s.EquippedCards[3] = (int)CardId.PlasmaCannon;

            s.StartNewRun(1);
            s.Combat.CurrentWave = 85;
            s.Combat.GoldenTower.CycleTimer = 0.2f;
            s.Combat.BlackHole.CycleTimer = 0.2f;
            s.Combat.GoldenBot.CycleTimer = BalanceTables.GOLDEN_BOT_COOLDOWN_SEC - 1.0f;
            s.Combat.GemDrone.NextSpawnRealTimer = 0.1f;
            s.RunCash = 62400.0;
            return s;
        }

        private static GameSession BuildScenario05_HangarMeta()
        {
            var s = BuildScenario04_Wave85GtBhMolten();
            s.EndCurrentRunToHangar();
            s.BestWaveT1 = 92;
            s.Coins = 74250;
            s.Gems = 185;
            s.Stones = 35;
            s.EliteCells = 24;
            s.UnlockedLabSlots = 2;
            s.TryStartLab(LabId.GameSpeed, 0);
            s.TryBoostLabSlotWithCells(0, boost3x: false);
            s.TryStartLab(LabId.BlackHoleCoinBonus, 1);
            return s;
        }

        private static void EnsureShowcaseBossAndAction(CombatSim sim)
        {
            // Place Titanium ATM Boss in upper-right quadrant (completely clear of the diegetic Core HP plate below Core!)
            float bossAngle = 0.68f;
            float bossR = 1.28f;
            bool hasBoss = false;
            for (int i = 0; i < sim.ActiveEnemyCount; i++)
            {
                if (sim.Enemies[i].Type == EnemyType.Boss)
                {
                    sim.Enemies[i].Angle = bossAngle;
                    sim.Enemies[i].RadiusDist = bossR;
                    sim.Enemies[i].PosX = Mathf.Cos(bossAngle) * bossR;
                    sim.Enemies[i].PosY = Mathf.Sin(bossAngle) * bossR;
                    sim.Enemies[i].HitFlashTimer = 0.0f;
                    hasBoss = true;
                    break;
                }
            }

            if (!hasBoss && sim.ActiveEnemyCount < CombatSim.ENEMY_CAPACITY - 1)
            {
                int idx = sim.ActiveEnemyCount++;
                sim.ActiveBossCount++;
                sim.Enemies[idx] = new EnemyState
                {
                    Id = 9991,
                    Type = EnemyType.Boss,
                    Angle = bossAngle,
                    RadiusDist = bossR,
                    PosX = Mathf.Cos(bossAngle) * bossR,
                    PosY = Mathf.Sin(bossAngle) * bossR,
                    Hp = 850,
                    MaxHp = 1200,
                    BaseDamage = 45,
                    SpeedSim = 0.35f,
                    Mass = 10f,
                    HitboxRadius = 0.38f,
                    AttackCooldown = 0.05f,
                    HitFlashTimer = 0.0f
                };
            }

            // Inject fresh shred kills so hot Voronoi chunks, HDR welding sparks, and gold ingots burst mid-air!
            PushShowcaseKill(sim, EnemyType.Tank, -1.10f, 0.82f, orbOrRay: false, inGolden: false, coins: 12);
            PushShowcaseKill(sim, EnemyType.Fast, 0.92f, -0.78f, orbOrRay: false, inGolden: false, coins: 8);
        }

        private static void EnsureShowcaseDeathRayAndVampire(CombatSim sim)
        {
            sim.DeathRay.Equipped = true;
            sim.DeathRay.Active = true;
            sim.DeathRay.TimeRemaining = 3.5f;
            sim.DeathRay.Angle = -0.34f; // Sweeps cleanly across lower-right enemy wave

            if (sim.ActiveEnemyCount < CombatSim.ENEMY_CAPACITY - 1)
            {
                int idx = sim.ActiveEnemyCount++;
                sim.ActiveEliteCount++;
                float ang = 2.45f; // Upper-left quadrant: clear crimson siphon beam + orange Magnetic Garlic rings!
                float r = sim.Stats.RangeSim * 0.64f;
                sim.Enemies[idx] = new EnemyState
                {
                    Id = 9992,
                    Type = EnemyType.Vampire,
                    Angle = ang,
                    RadiusDist = r,
                    PosX = Mathf.Cos(ang) * r,
                    PosY = Mathf.Sin(ang) * r,
                    Hp = 4200,
                    MaxHp = 5000,
                    BaseDamage = 120,
                    SpeedSim = 0.55f,
                    Mass = 2.5f,
                    HitboxRadius = 0.24f,
                    IsVampireBeaming = true
                };
                sim.AnyVampireBeaming = true;
            }

            float drEndX = Mathf.Cos(-0.34f) * (sim.Stats.RangeSim * 0.85f);
            float drEndY = Mathf.Sin(-0.34f) * (sim.Stats.RangeSim * 0.85f);
            PushShowcaseKill(sim, EnemyType.Tank, drEndX, drEndY, orbOrRay: true, inGolden: false, coins: 35);
            PushShowcaseKill(sim, EnemyType.Basic, 0.15f, sim.Stats.RangeSim * 0.90f, orbOrRay: true, inGolden: false, coins: 18);
        }

        private static void EnsureShowcaseSyncedOverdrive(CombatSim sim)
        {
            sim.GoldenTower.Unlocked = true;
            sim.GoldenTower.Active = true;
            sim.GoldenTower.TimeRemaining = 14.0f;

            // Place Black Hole in upper-left quadrant (completely unobstructed by upper-right Gem Drone UI button!)
            sim.BlackHole.Unlocked = true;
            sim.BlackHole.Active = true;
            sim.BlackHole.TimeRemaining = 14.0f;
            sim.BlackHole.OrbitAngle = 2.48f;
            float bhR = sim.Stats.RangeSim * 0.88f;
            sim.BlackHole.PosX = Mathf.Cos(sim.BlackHole.OrbitAngle) * bhR;
            sim.BlackHole.PosY = Mathf.Sin(sim.BlackHole.OrbitAngle) * bhR;
            if (sim.BlackHole.RadiusSim < 0.85f) sim.BlackHole.RadiusSim = 0.92f;

            // Pull 3 enemies tightly around the Black Hole accretion rim for authentic vortex clustering
            for (int i = 0; i < Math.Min(3, sim.ActiveEnemyCount); i++)
            {
                float offA = i * 2.09f;
                sim.Enemies[i].PosX = sim.BlackHole.PosX + Mathf.Cos(offA) * 0.34f;
                sim.Enemies[i].PosY = sim.BlackHole.PosY + Mathf.Sin(offA) * 0.34f;
                sim.Enemies[i].RadiusDist = Mathf.Sqrt(sim.Enemies[i].PosX * sim.Enemies[i].PosX + sim.Enemies[i].PosY * sim.Enemies[i].PosY);
            }

            // Golden Bot in lower-left quadrant with glowing golden field ring
            sim.GoldenBot.Unlocked = true;
            sim.GoldenBot.PulseActive = true;
            sim.GoldenBot.PosX = -1.25f;
            sim.GoldenBot.PosY = -0.82f;
            if (sim.GoldenBot.RadiusSim < 0.8f) sim.GoldenBot.RadiusSim = 0.95f;

            // Clickable Purple Gem Drone in lower-right quadrant
            sim.GemDrone.Active = true;
            sim.GemDrone.RealTimeRemaining = 12.0f;
            sim.GemDrone.PosX = 1.48f;
            sim.GemDrone.PosY = -0.82f;

            // Active jagged electric cyan Chain Lightning arcs across right-side enemies
            sim.ActiveChainArcCount = 0;
            float rBase = sim.Stats.RangeSim * 0.78f;
            Vector2 p0 = new Vector2(0.32f, 0.24f);
            Vector2 p1 = new Vector2(rBase * 0.72f, rBase * 0.52f);
            Vector2 p2 = new Vector2(rBase * 0.98f, rBase * 0.08f);
            Vector2 p3 = new Vector2(rBase * 0.76f, -rBase * 0.44f);
            sim.ChainArcs[sim.ActiveChainArcCount++] = new ChainArcState { FromX = p0.x, FromY = p0.y, ToX = p1.x, ToY = p1.y, TimeRemaining = 0.20f };
            sim.ChainArcs[sim.ActiveChainArcCount++] = new ChainArcState { FromX = p1.x, FromY = p1.y, ToX = p2.x, ToY = p2.y, TimeRemaining = 0.20f };
            sim.ChainArcs[sim.ActiveChainArcCount++] = new ChainArcState { FromX = p2.x, FromY = p2.y, ToX = p3.x, ToY = p3.y, TimeRemaining = 0.20f };

            // Shower of 24K Gold Ingots + Voronoi chunks from Black Hole + Golden Tower combo!
            PushShowcaseKill(sim, EnemyType.Tank, sim.BlackHole.PosX + 0.18f, sim.BlackHole.PosY - 0.12f, orbOrRay: true, inGolden: true, coins: 180);
            PushShowcaseKill(sim, EnemyType.Ranged, p1.x, p1.y, orbOrRay: false, inGolden: true, coins: 140);
        }

        private static void PushShowcaseKill(CombatSim sim, EnemyType type, float x, float y, bool orbOrRay, bool inGolden, double coins)
        {
            if (sim.KillEventCount >= sim.KillEvents.Length) return;
            sim.KillEvents[sim.KillEventCount++] = new KillEvent
            {
                Type = type,
                PosX = x,
                PosY = y,
                KilledByOrbOrRay = orbOrRay,
                KilledInGolden = inGolden,
                CashEarned = coins * 2.0,
                CoinsEarned = coins,
                CellsEarned = type == EnemyType.Vampire ? 2 : 0
            };
        }

        private static void WriteGateReport(string qaDir, List<FrameStats> frames)
        {
            var sb = new StringBuilder(1280);
            var ic = CultureInfo.InvariantCulture;
            sb.Append("{\n");
            sb.Append("  \"gate\": \"STOP-GATE 2\",\n");
            sb.AppendFormat(ic, "  \"resolution\": \"{0}x{1}\",\n", WIDTH, HEIGHT);
            sb.AppendFormat(ic, "  \"graphics_device\": \"{0}\",\n", SystemInfo.graphicsDeviceType);
            sb.Append("  \"frames\": [\n");
            for (int i = 0; i < frames.Count; i++)
            {
                FrameStats f = frames[i];
                sb.Append("    { ");
                sb.AppendFormat(ic,
                    "\"file\": \"{0}\", \"bytes\": {1}, \"world_mean_luma\": {2:F4}, \"world_std_luma\": {3:F4}, \"final_mean_luma\": {4:F4}, \"final_std_luma\": {5:F4}, \"magenta_ratio\": {6:F6}, \"passed\": {7}",
                    f.FileName, f.Bytes, f.WorldMeanLuma, f.WorldStdLuma, f.FinalMeanLuma, f.FinalStdLuma, f.MagentaRatio, f.Passed ? "true" : "false");
                sb.Append(i + 1 < frames.Count ? " },\n" : " }\n");
            }
            sb.Append("  ]\n}\n");

            File.WriteAllText(Path.Combine(qaDir, "gate2_report.json"), sb.ToString(), new UTF8Encoding(false));
        }
    }
}
