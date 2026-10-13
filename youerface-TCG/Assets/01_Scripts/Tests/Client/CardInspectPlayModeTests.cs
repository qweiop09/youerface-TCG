using System.Collections;
using NUnit.Framework;
using TCG.Client.Battle;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TCG.Tests.Client
{
    /// <summary>카드 확인 창: 열면 맨 위에서 뒤를 막고, 닫으면 막지 않는다. 짧은 클릭 판정 자체(포인터)는 사람이 확인한다.</summary>
    public class CardInspectPlayModeTests
    {
        [UnityTest]
        public IEnumerator Inspect_OpensOnTop_AndCloses()
        {
            SceneManager.LoadScene("Battle");
            yield return null;
            yield return null;

            var bootstrap = Object.FindAnyObjectByType<BattleBootstrap>();
            bootstrap.Spectate = true; // 보는 동안에도 대전은 계속 진행된다
            bootstrap.Speed = 4f;
            var inspect = bootstrap.Board.Hud.Inspect;
            var card = bootstrap.State.GetPlayer(BattleBootstrap.Viewer).Hand.Entities[0];

            inspect.Show(card);
            Assert.That(inspect.IsOpen, Is.True);
            Assert.That(inspect.ShownEntityId, Is.EqualTo(card.Id));
            Assert.That(inspect.transform.GetSiblingIndex(), Is.EqualTo(inspect.transform.parent.childCount - 1), "다른 HUD 요소보다 위");
            Assert.That(inspect.GetComponent<CanvasGroup>().blocksRaycasts, Is.True);

            int applied = bootstrap.CommandsApplied;
            float started = Time.realtimeSinceStartup;
            while (bootstrap.CommandsApplied == applied && Time.realtimeSinceStartup - started < 20f)
                yield return null;
            Assert.That(bootstrap.CommandsApplied, Is.GreaterThan(applied), "보는 동안에도 진행");
            Assert.That(inspect.IsOpen, Is.True, "자동으로 닫히지 않음");

            inspect.Close();
            Assert.That(inspect.IsOpen, Is.False);
            Assert.That(inspect.GetComponent<CanvasGroup>().blocksRaycasts, Is.False);
            yield return new WaitForSeconds(0.3f);
            Assert.That(inspect.GetComponent<CanvasGroup>().alpha, Is.EqualTo(0f));
        }
    }
}
