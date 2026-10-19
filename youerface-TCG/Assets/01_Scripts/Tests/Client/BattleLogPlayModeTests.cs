using System.Collections;
using System.Linq;
using NUnit.Framework;
using TCG.Client.Battle;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TCG.Tests.Client
{
    /// <summary>M5.5-2: 행동 기록 패널·체인 표시 — 컴퓨터끼리 몇 턴 두는 동안 기록이 순서대로 쌓이고, 체인 카드는 체인이 끝나면 비워진다.</summary>
    public class BattleLogPlayModeTests
    {
        [UnityTest]
        [Timeout(300000)]
        public IEnumerator Log_RecordsTurnsAndPlays_ChainClearsAfterwards()
        {
            SceneManager.LoadScene("Battle");
            yield return null;
            yield return null;

            var bootstrap = Object.FindAnyObjectByType<BattleBootstrap>();
            bootstrap.Spectate = true;
            bootstrap.Speed = 10f;
            var log = bootstrap.Board.Hud.Log;
            var chain = bootstrap.Board.Hud.Chain;

            int maxChain = 0;
            float started = Time.realtimeSinceStartup;
            while (bootstrap.Session.InProgress && bootstrap.State.TurnNumber < 8 && Time.realtimeSinceStartup - started < 120f)
            {
                maxChain = Mathf.Max(maxChain, chain.Count);
                yield return null;
            }
            float drain = Time.realtimeSinceStartup;
            while (bootstrap.Player.IsPlaying && Time.realtimeSinceStartup - drain < 30f)
            {
                maxChain = Mathf.Max(maxChain, chain.Count);
                yield return null;
            }

            var lines = log.Lines;
            Debug.Log($"[Test] log {lines.Count} lines, max chain {maxChain}\n" + string.Join("\n", lines.Take(40)));
            Assert.That(lines.Count, Is.GreaterThan(10));
            Assert.That(lines[0], Does.Contain("대전 시작"));
            Assert.That(lines[1], Does.Contain("코인 토스:"), "코인 토스가 끝난 뒤 선공 결과");
            Assert.That(lines[2], Does.Contain("턴 1 ·"), "첫 턴 시작도 기록");
            Assert.That(lines.Any(l => l.StartsWith("상대: 카드 뽑음:")), Is.False, "상대가 뽑은 카드 이름은 쓰지 않음");
            Assert.That(lines.Any(l => l.Contains(" 냄") || l.Contains(" 사용")), Is.True, "카드 사용 기록");
            int turn2 = lines.ToList().FindIndex(l => l.Contains("턴 2 ·"));
            int turn1 = lines.ToList().FindIndex(l => l.Contains("턴 1 ·"));
            Assert.That(turn1, Is.LessThan(turn2), "턴 순서대로");

            if (lines.Any(l => l.Contains("체인 1 발동")))
                Assert.That(maxChain, Is.GreaterThan(0), "체인이 생기면 카드로 표시");
            if (bootstrap.State.Decision == null)
                Assert.That(chain.Count, Is.EqualTo(0), "체인이 끝나면 비움");

            log.Toggle();
            Assert.That(log.Expanded, Is.False);
            log.Toggle();
            Assert.That(log.Expanded, Is.True);
        }
    }
}
