using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using TCG.Client.Battle;
using TCG.Core.State;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TCG.Tests.Client
{
    /// <summary>
    /// Battle 장면을 관전 모드로 돌리며 연출 재생기를 검사한다 (PlayMode).
    /// - 연출 중 예외·에러 로그가 없어야 한다 (Unity 테스트는 에러 로그가 있으면 실패).
    /// - 매 묶음이 끝날 때 화면 표시(카드·숫자·영웅 체력)가 실제 상태와 같아야 한다 (TotalDrift = 0).
    /// 환경 변수 TCG_CAPTURE_DIR을 주면 연출 중간 장면을 PNG로 남긴다 (TCG_CAPTURE_SPEED로 배속 지정).
    /// </summary>
    public class PresentationPlayModeTests
    {
        [UnityTest]
        public IEnumerator Spectate_PresentationsStayInSyncWithState()
        {
            SceneManager.LoadScene("Battle");
            yield return null;
            yield return null;

            var bootstrap = UnityEngine.Object.FindAnyObjectByType<BattleBootstrap>();
            Assert.That(bootstrap, Is.Not.Null);
            string captureDir = Environment.GetEnvironmentVariable("TCG_CAPTURE_DIR");
            string speed = Environment.GetEnvironmentVariable("TCG_CAPTURE_SPEED");
            bootstrap.Speed = captureDir != null && speed != null ? float.Parse(speed) : 8f;

            float started = Time.realtimeSinceStartup;
            float nextCapture = started + 2f;
            int captured = 0;
            while (bootstrap.State.Phase == GamePhase.InProgress && bootstrap.CommandsApplied < 160
                   && Time.realtimeSinceStartup - started < 300f)
            {
                if (captureDir != null && captured < 60 && Time.realtimeSinceStartup >= nextCapture)
                {
                    Capture(Path.Combine(captureDir, $"frame_{captured++:00}.png"));
                    nextCapture = Time.realtimeSinceStartup + 0.35f;
                }
                yield return null;
            }

            float drainStart = Time.realtimeSinceStartup;
            while (bootstrap.Player.IsPlaying && Time.realtimeSinceStartup - drainStart < 60f)
                yield return null;

            if (captureDir != null)
                Capture(Path.Combine(captureDir, "final.png"));

            Assert.That(bootstrap.Player.IsPlaying, Is.False, "연출이 끝나지 않음");
            Assert.That(bootstrap.Player.BatchesPlayed, Is.GreaterThan(20));
            Assert.That(bootstrap.Player.TotalDrift, Is.EqualTo(0), "연출 후 화면과 상태가 달랐던 곳 수");
            Debug.Log($"[Test] commands {bootstrap.CommandsApplied}, queued {bootstrap.Player.BatchesQueued}, played {bootstrap.Player.BatchesPlayed}, phase {bootstrap.State.Phase}");
        }

        static void Capture(string path)
        {
            var camera = Camera.main;
            var rt = new RenderTexture(1280, 720, 24);
            var previous = camera.targetTexture;
            camera.targetTexture = rt;
            camera.Render();
            RenderTexture.active = rt;
            var image = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            image.Apply();
            RenderTexture.active = null;
            camera.targetTexture = previous;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, image.EncodeToPNG());
            UnityEngine.Object.Destroy(image);
            rt.Release();
        }
    }
}
