using System.Collections;
using NUnit.Framework;
using TCG.Client.Battle;
using TCG.Client.Flow;
using TCG.Core.AI;
using TCG.Core.State;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TCG.Tests.Client
{
    /// <summary>
    /// 싱글 배틀 흐름: 시작 화면 → 난이도 선택 → 대전(관전으로 끝까지) → 결과 화면 → 다시 하기 → 난이도 선택으로.
    /// 버튼은 onClick을 직접 불러 누른 것과 같은 경로로 진행한다.
    /// </summary>
    public class FlowPlayModeTests
    {
        [TearDown]
        public void ResetLaunch()
        {
            BattleLaunch.Difficulty = AiDifficulty.Normal;
            BattleLaunch.DeckKey = null;
        }

        [UnityTest]
        [Timeout(900000)]
        public IEnumerator Title_SelectDifficulty_Battle_Result_Rematch_BackToTitle()
        {
            BattleLaunch.Difficulty = AiDifficulty.Normal;
            SceneManager.LoadScene(BattleLaunch.TitleScene);
            yield return null;
            yield return null;

            var title = Object.FindAnyObjectByType<TitleScreen>();
            Assert.That(title, Is.Not.Null);
            Assert.That(title.Selected, Is.EqualTo(AiDifficulty.Normal), "마지막 난이도가 기본 선택");
            string dir = System.Environment.GetEnvironmentVariable("TCG_CAPTURE_DIR");
            title.OptionButton(AiDifficulty.Easy).onClick.Invoke();
            Assert.That(title.Selected, Is.EqualTo(AiDifficulty.Easy));
            if (dir != null)
            {
                yield return null;
                PresentationPlayModeTests.Capture(System.IO.Path.Combine(dir, "title.png"));
            }
            title.StartButton.onClick.Invoke();
            yield return null;
            yield return null;

            var bootstrap = Object.FindAnyObjectByType<BattleBootstrap>();
            Assert.That(bootstrap, Is.Not.Null);
            Assert.That(bootstrap.OpponentDifficulty, Is.EqualTo(AiDifficulty.Easy), "고른 난이도로 상대 컴퓨터");
            Assert.That(bootstrap.Result, Is.Null);
            bootstrap.Spectate = true;
            bootstrap.Speed = 10f;

            float started = Time.realtimeSinceStartup;
            while (bootstrap.Result == null && Time.realtimeSinceStartup - started < 300f)
                yield return null;

            Assert.That(bootstrap.Result, Is.Not.Null, "대전이 끝나면 결과 화면");
            Assert.That(bootstrap.State.Phase, Is.EqualTo(GamePhase.Finished));
            Assert.That(bootstrap.Player.IsPlaying, Is.False, "결과 화면은 연출이 다 끝난 뒤");
            Assert.That(bootstrap.Player.TotalDrift, Is.EqualTo(0));
            Assert.That(bootstrap.Result.Title, Is.EqualTo(ResultPanel.TitleFor(bootstrap.State, BattleBootstrap.Viewer)));
            if (dir != null)
            {
                yield return null;
                PresentationPlayModeTests.Capture(System.IO.Path.Combine(dir, "result.png"));
            }

            bootstrap.Result.RematchButton.onClick.Invoke();
            yield return null;
            yield return null;

            var again = Object.FindAnyObjectByType<BattleBootstrap>();
            Assert.That(again, Is.Not.Null);
            Assert.That(again, Is.Not.SameAs(bootstrap), "새 판");
            Assert.That(again.OpponentDifficulty, Is.EqualTo(AiDifficulty.Easy), "다시 하기는 같은 난이도");
            Assert.That(again.Session.InProgress, Is.True);
            Assert.That(again.Result, Is.Null);

            BattleLaunch.ToTitle();
            yield return null;
            yield return null;

            var back = Object.FindAnyObjectByType<TitleScreen>();
            Assert.That(back, Is.Not.Null);
            Assert.That(back.Selected, Is.EqualTo(AiDifficulty.Easy), "돌아오면 마지막 난이도가 선택되어 있음");
        }
    }
}
