using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using TCG.Client.Battle;
using TCG.Client.Flow;
using TCG.Client.Online;
using TCG.Core.Commands;
using TCG.Core.Net;
using TCG.Core.State;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TCG.Tests.Client
{
    /// <summary>M6-3 온라인 방 화면 → 가짜 연결 대전 (컴퓨터 상대). 캡처는 TCG_CAPTURE_DIR이 있을 때만.</summary>
    public class OnlinePlayModeTests
    {
        [TearDown]
        public void TearDown()
        {
            OnlineRunner.Current?.Close();
            BattleLaunch.DeckKey = null;
            OnlineScreen.LastMaxScore = null;
        }

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

        static IEnumerator OpenOnlineScreen(Action<OnlineScreen> got)
        {
            SceneManager.LoadScene(OnlineScreen.SceneName);
            yield return null;
            yield return null;
            var screen = UnityEngine.Object.FindAnyObjectByType<OnlineScreen>();
            Assert.That(screen, Is.Not.Null);
            got(screen);
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator HostTest_RoomToBattle_HidesOpponentHand_ConcedeShowsResult()
        {
            OnlineScreen screen = null;
            yield return OpenOnlineScreen(s => screen = s);
            Assert.That(screen.CreateRoomButton.interactable, Is.False, "UGS 전에는 꺼 둠");
            screen.SetMaxScore(null);
            StringAssert.Contains("맞습니다", screen.DeckCheckText);

            string dir = Environment.GetEnvironmentVariable("TCG_CAPTURE_DIR");
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
                yield return CardMakerPlayModeTests.CaptureScreen(Path.Combine(dir, "online-room.png"));
            }

            screen.HostTestButton.onClick.Invoke();
            BattleBootstrap bootstrap = null;
            yield return WaitFor(() => (bootstrap = UnityEngine.Object.FindAnyObjectByType<BattleBootstrap>()) != null && bootstrap.Session != null, 10f, "대전 화면");
            Assert.That(bootstrap.IsOnline, Is.True);
            Assert.That(bootstrap.ViewerSeat, Is.EqualTo(OnlineHost.HostSeat));
            Assert.That(bootstrap.Session.Online, Is.True);

            var state = bootstrap.State;
            foreach (var e in state.GetPlayer(1).Hand.Entities)
                Assert.That(HiddenCard.Is(e), Is.True, "상대 손패는 시야 상태에서 가려짐");
            foreach (var e in state.GetPlayer(0).Hand.Entities)
                Assert.That(HiddenCard.Is(e), Is.False);
            yield return WaitFor(() => bootstrap.Board.Hud.OnlineText.Length > 0, 5f, "시간 표시");
            StringAssert.Contains(":", bootstrap.Board.Hud.OnlineText);

            // 몇 턴 진행: 내 차례면 턴 종료, 컴퓨터는 알아서 둔다.
            int turns = 0;
            float start = Time.realtimeSinceStartup;
            while (turns < 3 && Time.realtimeSinceStartup - start < 60f)
            {
                var session = bootstrap.Session;
                if (session.HumanCanAct && session.State.Decision == null)
                {
                    Assert.That(session.TrySubmit(new EndTurnCommand(bootstrap.ViewerSeat), out var reason), Is.True, reason);
                    turns++;
                }
                else if (session.HumanCanAct && session.State.Decision != null)
                {
                    session.TrySubmit(HostMatch.AutoCommand(session.State, bootstrap.ViewerSeat), out _);
                }
                yield return null;
            }
            Assert.That(turns, Is.EqualTo(3), "온라인으로 턴이 오간다");

            if (!string.IsNullOrEmpty(dir))
            {
                yield return WaitFor(() => !bootstrap.Player.IsPlaying, 10f, "연출 끝");
                PresentationPlayModeTests.Capture(Path.Combine(dir, "online-battle.png"));
            }

            Assert.That(OnlineRunner.Current.Link.Submit(new ConcedeCommand(bootstrap.ViewerSeat), out _), Is.True);
            yield return WaitFor(() => bootstrap.Result != null, 20f, "결과 화면");
            Assert.That(bootstrap.Result.Title, Is.EqualTo("패배"));
            Assert.That(OnlineRunner.Current.Link.EndReason, Is.EqualTo(MatchEndReason.Conceded));
            bootstrap.Result.TitleButton.onClick.Invoke();
            yield return null;
            Assert.That(OnlineRunner.Current, Is.Null, "나가면 연결 정리");
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator GuestTest_ViewerIsSeatOne_DropAndRejoin()
        {
            OnlineScreen screen = null;
            yield return OpenOnlineScreen(s => screen = s);
            screen.SetMaxScore(null);
            screen.GuestTestButton.onClick.Invoke();

            BattleBootstrap bootstrap = null;
            yield return WaitFor(() => (bootstrap = UnityEngine.Object.FindAnyObjectByType<BattleBootstrap>()) != null && bootstrap.Session != null, 10f, "대전 화면");
            Assert.That(bootstrap.ViewerSeat, Is.EqualTo(OnlineHost.GuestSeat));
            Assert.That(bootstrap.Board.Viewer, Is.EqualTo(OnlineHost.GuestSeat), "손님은 자기 자리가 아래쪽");
            foreach (var e in bootstrap.State.GetPlayer(0).Hand.Entities)
                Assert.That(HiddenCard.Is(e), Is.True);

            var runner = OnlineRunner.Current;
            runner.DropGuestLink();
            yield return WaitFor(() => runner.Link.ConnectionNote != null, 5f, "끊김 표시");
            StringAssert.Contains("재접속", runner.Link.ConnectionNote);
            yield return WaitFor(() => runner.Guest.Phase == GuestPhase.Playing && runner.Link.ConnectionNote == null, 10f, "자동 재접속");
            Assert.That(runner.Guest.View.Phase, Is.EqualTo(GamePhase.InProgress));

            // 재접속 뒤에도 명령이 통한다: 내 차례가 오면 턴 종료.
            yield return WaitFor(() => bootstrap.Session.HumanCanAct && bootstrap.State.Decision == null, 30f, "내 차례");
            int turn = bootstrap.State.TurnNumber;
            Assert.That(bootstrap.Session.TrySubmit(new EndTurnCommand(OnlineHost.GuestSeat), out var reason), Is.True, reason);
            yield return WaitFor(() => bootstrap.State.TurnNumber > turn, 10f, "턴 넘어감");
        }
    }
}
