using System.Collections;
using NUnit.Framework;
using TCG.Client.Battle;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TCG.Tests.Client
{
    /// <summary>M5.5-2: 대전 시작 코인 토스 연출 — 한 번 보여 주고, 끝날 때까지 행동을 막고, 결과 문구가 실제 선공과 맞는다.</summary>
    public class CoinTossPlayModeTests
    {
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Battle_ShowsCoinTossBeforeFirstAction()
        {
            SceneManager.LoadScene("Battle");
            yield return null;
            yield return null;

            var bootstrap = Object.FindAnyObjectByType<BattleBootstrap>();
            Assert.That(bootstrap, Is.Not.Null);
            bootstrap.Spectate = false;
            bootstrap.Speed = 4f;
            var coin = bootstrap.Board.Hud.CoinToss;
            Assert.That(bootstrap.State.TurnNumber, Is.EqualTo(1));
            bool mineFirst = bootstrap.State.ActivePlayerIndex == bootstrap.Board.Viewer;
            Assert.That(coin.ShownCount, Is.EqualTo(1), "대전 시작 시 코인 토스 1번");
            Assert.That(bootstrap.Player.IsPlaying, Is.True);
            Assert.That(bootstrap.Session.HumanCanAct, Is.False, "코인 토스 중에는 행동 불가");

            float started = Time.realtimeSinceStartup;
            while (coin.IsShowing || bootstrap.Player.IsPlaying)
            {
                Assert.That(Time.realtimeSinceStartup - started, Is.LessThan(20f), "코인 토스가 끝나지 않음");
                yield return null;
            }

            Assert.That(coin.TitleText, Is.EqualTo(mineFirst ? "선공입니다" : "후공입니다"));
            Assert.That(coin.ShownCount, Is.EqualTo(1));
        }
    }
}
