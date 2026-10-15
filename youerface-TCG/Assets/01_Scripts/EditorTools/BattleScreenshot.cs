using System;
using System.IO;
using TCG.Client.Battle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TCG.EditorTools
{
    /// <summary>
    /// Play 없이 Battle 장면을 만들어 PNG로 찍는다 (개발 중 화면 확인용).
    /// Unity -batchmode -projectPath ... -executeMethod TCG.EditorTools.BattleScreenshot.CaptureFromCommandLine
    ///       -screenshot out.png [-seed 2026] [-steps 45] [-width 1920] [-height 1080]
    /// -nographics 없이 실행해야 그려진다.
    /// </summary>
    public static class BattleScreenshot
    {
        [MenuItem("TCG/Capture Battle Screenshot")]
        public static void CaptureFromMenu()
        {
            Capture("Logs/battle.png", 2026, 45, 1920, 1080);
            EditorUtility.RevealInFinder("Logs/battle.png");
        }

        public static void CaptureFromCommandLine()
        {
            int exitCode = 0;
            try
            {
                string path = Arg("-screenshot", "Logs/battle.png");
                Capture(path, int.Parse(Arg("-seed", "2026")), int.Parse(Arg("-steps", "45")),
                    int.Parse(Arg("-width", "1920")), int.Parse(Arg("-height", "1080")), Arg("-pose", ""));
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                exitCode = 1;
            }
            EditorApplication.Exit(exitCode);
        }

        /// <param name="pose">"hover" = 내 손패 첫 카드를 호버 자세로, "target" = 대상 지정 자세로 (화면 확인용).</param>
        public static void Capture(string path, int seed, int steps, int width, int height, string pose = "")
        {
            EditorSceneManager.OpenScene(ProjectSetup.BattleScenePath, OpenSceneMode.Single);
            var bootstrap = UnityEngine.Object.FindAnyObjectByType<BattleBootstrap>();
            bootstrap.Configure(seed, steps);
            bootstrap.Build();
            Canvas.ForceUpdateCanvases();

            var camera = Camera.main;
            camera.aspect = (float)width / height;
            ApplyPose(bootstrap.Board, pose);
            var rt = new RenderTexture(width, height, 24) { antiAliasing = 4 };
            var previousTarget = camera.targetTexture;
            camera.targetTexture = rt;
            camera.aspect = (float)width / height;

            // 방금 만든 머티리얼의 값이 SRP Batcher 버퍼에 반영되지 않은 채 그려지는 것을 피하려고
            // 캡처 동안 배처를 끄고, 한 번 그려서 데우고 다시 그린다.
            var pipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            bool batcher = pipeline != null && pipeline.useSRPBatcher;
            if (pipeline != null)
                pipeline.useSRPBatcher = false;
            camera.Render();
            camera.Render();
            if (pipeline != null)
                pipeline.useSRPBatcher = batcher;

            var previousActive = RenderTexture.active;
            RenderTexture.active = rt;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();
            RenderTexture.active = previousActive;
            camera.targetTexture = previousTarget;

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            File.WriteAllBytes(path, image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
            rt.Release();
            Debug.Log($"[Screenshot] Saved {Path.GetFullPath(path)}");
        }

        static void ApplyPose(TCG.Client.Board.BoardView board, string pose)
        {
            if (pose == "inspect")
            {
                InspectSample(board);
                return;
            }
            var card = pose == "hover" ? board.BottomHandCard(1) ?? board.BottomHandCard(0) : board.BottomHandCard(0);
            if (card == null || string.IsNullOrEmpty(pose))
                return;
            Vector3 position;
            Quaternion rotation;
            float scale = 1f;
            if (pose == "hover")
            {
                board.HoveredHandCard = card;
                board.HoverPose(card, out position, out rotation, out scale);
                // 옆 카드가 비켜난 자리도 바로 보여 준다.
                for (int i = 0; board.BottomHandCard(i) != null; i++)
                {
                    var other = board.BottomHandCard(i);
                    if (other != card && board.TargetPose(other, out var p, out var r))
                        other.transform.SetPositionAndRotation(p, r);
                }
            }
            else if (pose == "target")
            {
                board.TargetingPose(out position, out rotation);
                scale = TCG.Client.Board.BoardView.TargetingScale;
            }
            else
                return;
            card.transform.SetPositionAndRotation(position, rotation);
            card.transform.localScale = Vector3.one * scale;
        }

        /// <summary>
        /// 카드 확인 창을 바로 띄운다: 양쪽 손패·덱·보드에서 소환물을 만드는 카드 → 키워드 + 효과 여러 개 → 키워드 카드 순으로 고른다.
        /// </summary>
        static void InspectSample(TCG.Client.Board.BoardView board)
        {
            var state = UnityEngine.Object.FindAnyObjectByType<BattleBootstrap>().Session.State;
            TCG.Core.State.Entity pick = null;
            int Rank(TCG.Core.State.Entity e)
            {
                var effects = e.Definition.Effects;
                if (effects != null && effects.Exists(x => x?.Action?.Type == TCG.Core.Data.ActionType.CreateSummon)) return 3;
                if (e.Keywords.Count > 0 && effects != null && effects.Count > 1) return 2;
                if (e.Keywords.Count > 0) return 1;
                return 0;
            }
            for (int p = 0; p < 2; p++)
            {
                var player = state.GetPlayer(p);
                foreach (var zone in new[] { player.Hand, player.Deck, player.Minions, player.Companions, player.Field })
                {
                    foreach (var e in zone.Entities)
                    {
                        if (pick == null || Rank(e) > Rank(pick))
                            pick = e;
                    }
                }
            }
            if (pick != null)
                board.Hud.Inspect.ShowInstant(pick);
            Canvas.ForceUpdateCanvases();
            // 키워드 설명 창은 레이아웃 그룹으로 쌓으므로 프레임 없이 바로 계산시킨다.
            for (int i = 0; i < 3; i++)
                UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)board.Hud.Inspect.transform);
        }

        static string Arg(string name, string fallback)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name)
                    return args[i + 1];
            }
            return fallback;
        }
    }
}
