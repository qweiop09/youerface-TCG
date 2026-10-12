using System.Collections;
using NUnit.Framework;
using TCG.Client.Battle;
using TCG.Core.AI;
using TCG.Core.Commands;
using TCG.Core.State;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TCG.Tests.Client
{
    /// <summary>
    /// 사람 쪽 행동을 입력과 같은 경로(BattleSession.TrySubmit, 체인 선택 창)로 넣어 한 판을 끝까지 둔다.
    /// 포인터 조작 자체(끌기·화살표)는 사람이 직접 확인한다.
    /// </summary>
    public class HumanPlayModeTests
    {
        [UnityTest]
        public IEnumerator HumanPath_PlaysFullGame_ThroughSessionAndDecisionPanel()
        {
            SceneManager.LoadScene("Battle");
            yield return null;
            yield return null;

            var bootstrap = Object.FindAnyObjectByType<BattleBootstrap>();
            Assert.That(bootstrap, Is.Not.Null);
            bootstrap.Spectate = false;
            bootstrap.Speed = 10f;
            var session = bootstrap.Session;
            var helper = AiPlayers.Create(AiDifficulty.Normal, 99, session.Engine);
            int panelUses = 0;
            bool checkedLock = false;

            float started = Time.realtimeSinceStartup;
            while (session.InProgress && Time.realtimeSinceStartup - started < 300f)
            {
                if (session.HumanHasDecision)
                {
                    yield return null; // 창이 열리도록 한 프레임
                    if (session.HumanHasDecision)
                    {
                        Assert.That(bootstrap.Decisions.IsOpen, Is.True, "체인 기회 창이 열려야 함");
                        string dir = System.Environment.GetEnvironmentVariable("TCG_CAPTURE_DIR");
                        if (dir != null && panelUses == 0)
                        {
                            yield return null;
                            PresentationPlayModeTests.Capture(System.IO.Path.Combine(dir, "decision_panel.png"));
                        }
                        // 첫 선택지를 체인에 올린다 (없으면 패스).
                        if (bootstrap.Decisions.Options.Count > 0)
                            bootstrap.Decisions.ActivateOption(0);
                        else
                            bootstrap.Decisions.Pass();
                        panelUses++;
                    }
                }
                else if (session.HumanCanAct)
                {
                    string shotDir = System.Environment.GetEnvironmentVariable("TCG_CAPTURE_DIR");
                    if (shotDir != null && session.State.TurnNumber == 9 && !System.IO.File.Exists(System.IO.Path.Combine(shotDir, "my_turn.png")))
                        PresentationPlayModeTests.Capture(System.IO.Path.Combine(shotDir, "my_turn.png"));
                    var command = helper.ChooseCommand(session.State, session.Human);
                    Assert.That(session.TrySubmit(command, out var reason), Is.True, reason);

                    if (!checkedLock && session.Player.IsPlaying && session.InProgress)
                    {
                        Assert.That(session.TrySubmit(new EndTurnCommand(session.Human), out var lockReason), Is.False,
                            "연출 중에는 행동할 수 없어야 함");
                        Assert.That(lockReason, Does.Contain("연출"));
                        checkedLock = true;
                    }
                }
                yield return null;
            }

            float drainStart = Time.realtimeSinceStartup;
            while (session.Player.IsPlaying && Time.realtimeSinceStartup - drainStart < 60f)
                yield return null;

            Assert.That(session.State.Phase, Is.EqualTo(GamePhase.Finished));
            Assert.That(session.Player.TotalDrift, Is.EqualTo(0));
            Assert.That(checkedLock, Is.True);
            Debug.Log($"[Test] human path: commands {session.CommandsApplied}, panel uses {panelUses}, winner {session.State.WinnerIndex}");
        }
    }
}
