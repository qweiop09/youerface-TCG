using System;
using System.Collections;
using NUnit.Framework;
using TCG.Client.Battle;
using TCG.Core.Commands;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TCG.Tests.Client
{
    /// <summary>M5.5-2: 코스트 보석 — 상태와 같은 숫자, 카드 사용 시 소모, 손패 호버 미리보기, 턴 시작 채움.</summary>
    public class CostGemsPlayModeTests
    {
        static IEnumerator WaitFor(Func<bool> condition, float seconds, string what)
        {
            float start = Time.realtimeSinceStartup;
            while (!condition())
            {
                if (Time.realtimeSinceStartup - start > seconds)
                    Assert.Fail("시간 초과: " + what);
                yield return null;
            }
        }

        static void AssertSynced(BattleBootstrap bootstrap)
        {
            var gems = bootstrap.Board.Hud.Gems;
            var me = bootstrap.State.GetPlayer(bootstrap.Board.Viewer);
            var them = bootstrap.State.GetOpponent(bootstrap.Board.Viewer);
            Assert.That(gems.Available(0), Is.EqualTo(me.AvailableCost));
            Assert.That(gems.Max(0), Is.EqualTo(me.MaxCost));
            Assert.That(gems.Available(1), Is.EqualTo(them.AvailableCost));
            Assert.That(gems.Max(1), Is.EqualTo(them.MaxCost));
            Assert.That(gems.NumberText(0), Is.EqualTo($"{me.AvailableCost}/{me.MaxCost}"));
        }

        [UnityTest]
        [Timeout(180000)]
        public IEnumerator Gems_FollowCost_PreviewOnHover()
        {
            SceneManager.LoadScene("Battle");
            yield return null;
            yield return null;

            var bootstrap = UnityEngine.Object.FindAnyObjectByType<BattleBootstrap>();
            bootstrap.Spectate = false;
            bootstrap.Speed = 10f;
            var session = bootstrap.Session;
            int me = bootstrap.Board.Viewer;
            AssertSynced(bootstrap);

            // 내 차례가 될 때까지 (후공이면 상대 턴이 지나감).
            yield return WaitFor(() => session.HumanCanAct && session.State.Decision == null, 60f, "내 차례");
            AssertSynced(bootstrap);
            Assert.That(bootstrap.Board.Hud.Gems.Available(0), Is.GreaterThan(0), "턴 시작에 보석이 찼음");

            // 손패 호버 → 그 카드 코스트만큼 미리보기.
            var hand = session.State.GetPlayer(me).Hand.Entities;
            var hovered = bootstrap.Board.BottomHandCard(0);
            Assert.That(hovered, Is.Not.Null);
            bootstrap.Board.HoveredHandCard = hovered;
            yield return null;
            Assert.That(bootstrap.Board.Hud.Gems.PreviewCost, Is.EqualTo(hovered.Cost));
            bootstrap.Board.HoveredHandCard = null;
            yield return null;
            Assert.That(bootstrap.Board.Hud.Gems.PreviewCost, Is.EqualTo(-1));

            // 낼 수 있는 대상 없는 카드를 내면 보석이 소모된다.
            int before = session.State.GetPlayer(me).AvailableCost;
            PlayCardCommand play = null;
            for (int i = 0; i < hand.Count && play == null; i++)
            {
                var command = new PlayCardCommand(me, hand[i].Id);
                if (hand[i].Cost > 0 && session.Engine.Validate(session.State, command).IsValid)
                    play = command;
            }
            if (play != null)
            {
                Assert.That(session.TrySubmit(play, out var reason), Is.True, reason);
                yield return WaitFor(() => !bootstrap.Player.IsPlaying, 30f, "연출 끝");
                Assert.That(session.State.GetPlayer(me).AvailableCost, Is.LessThan(before));
                AssertSynced(bootstrap);
            }

            // 턴을 넘겼다 돌아오면 다시 찬다.
            int turn = session.State.TurnNumber;
            Assert.That(session.TrySubmit(new EndTurnCommand(me), out var endReason), Is.True, endReason);
            yield return WaitFor(() => session.State.TurnNumber > turn + 1 && session.HumanCanAct && session.State.Decision == null
                                       || !session.InProgress, 60f, "다시 내 차례");
            if (session.InProgress)
            {
                AssertSynced(bootstrap);
                Assert.That(bootstrap.Board.Hud.Gems.Available(0), Is.EqualTo(session.State.GetPlayer(me).MaxCost));
            }
        }
    }
}
