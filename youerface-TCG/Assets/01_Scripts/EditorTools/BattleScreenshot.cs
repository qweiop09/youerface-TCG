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
                    int.Parse(Arg("-width", "1920")), int.Parse(Arg("-height", "1080")));
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                exitCode = 1;
            }
            EditorApplication.Exit(exitCode);
        }

        public static void Capture(string path, int seed, int steps, int width, int height)
        {
            EditorSceneManager.OpenScene(ProjectSetup.BattleScenePath, OpenSceneMode.Single);
            var bootstrap = UnityEngine.Object.FindAnyObjectByType<BattleBootstrap>();
            bootstrap.Configure(seed, steps);
            bootstrap.Build();
            Canvas.ForceUpdateCanvases();

            var camera = Camera.main;
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
