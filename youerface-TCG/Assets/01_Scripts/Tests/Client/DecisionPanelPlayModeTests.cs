using System.Collections;
using NUnit.Framework;
using TCG.Client.Battle;
using TCG.Core.Commands;
using TCG.Core.Data;
using TCG.Core.State;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TCG.Tests.Client
{
    /// <summary>체인 기회 창: 강제(무조건 사용)와 선택(체인 걸 수 있음) 효과를 상자로 나눠 한 창에 보여 주는지.</summary>
    public class DecisionPanelPlayModeTests
    {
        static EffectDefinition OnCardPlayedHit(int amount, bool optional) => new EffectDefinition
        {
            Trigger = new TriggerDefinition { Type = TriggerType.OnCardPlayed },
            Activation = optional ? EffectActivation.Optional : EffectActivation.Mandatory,
            Target = new TargetDefinition { Type = TargetType.EnemyHero },
            Action = new ActionDefinition { Type = ActionType.Damage, Amount = amount },
        };

        static CardDefinition Companion(string name, params EffectDefinition[] effects)
        {
            var card = new CardDefinition { Id = "t_" + name, Name = name, Type = CardType.Companion, Cost = 1 };
            card.Effects.AddRange(effects);
            return card;
        }

        [UnityTest]
        public IEnumerator Panel_GroupsMandatoryAndOptional_InOneWindow()
        {
            SceneManager.LoadScene("Battle");
            yield return null;
            yield return null;
            var bootstrap = Object.FindAnyObjectByType<BattleBootstrap>();
            bootstrap.Spectate = false;
            bootstrap.Speed = 10f;
            var session = bootstrap.Session;

            float started = Time.realtimeSinceStartup;
            while (!(session.HumanCanAct && session.State.Decision == null) && Time.realtimeSinceStartup - started < 60f)
                yield return null;
            Assert.That(session.HumanCanAct, Is.True);

            // 시험용 상황: 내 동료 3장(카드 사용 시 강제 1개, 선택 2개) + 0코스트 기물 1장
            var state = session.State;
            var me = state.GetPlayer(session.Human);
            me.Companions.TryAdd(state.CreateEntity(session.Human, Companion("강제 교관", OnCardPlayedHit(1, false))));
            me.Companions.TryAdd(state.CreateEntity(session.Human, Companion("선택 교관 A", OnCardPlayedHit(1, true))));
            me.Companions.TryAdd(state.CreateEntity(session.Human, Companion("선택 교관 B", OnCardPlayedHit(2, true))));
            var minion = state.CreateEntity(session.Human, new CardDefinition { Id = "t_m", Name = "시험 기물", Type = CardType.Minion, Cost = 0, Attack = 1, Health = 1 });
            if (me.Hand.IsFull)
                me.Hand.Remove(me.Hand.Entities[0]);
            me.Hand.TryAdd(minion);
            bootstrap.Board.Render(state);

            Assert.That(session.TrySubmit(new PlayCardCommand(session.Human, minion.Id), out var reason), Is.True, reason);
            started = Time.realtimeSinceStartup;
            while (!bootstrap.Decisions.IsOpen && Time.realtimeSinceStartup - started < 30f)
                yield return null;

            Assert.That(bootstrap.Decisions.IsOpen, Is.True);
            Assert.That(bootstrap.Decisions.MandatoryShown, Is.EqualTo(1));
            Assert.That(bootstrap.Decisions.OptionalShown, Is.EqualTo(2));
            Assert.That(session.State.Decision.CanPass, Is.False);

            string dir = System.Environment.GetEnvironmentVariable("TCG_CAPTURE_DIR");
            if (dir != null)
            {
                yield return null;
                PresentationPlayModeTests.Capture(System.IO.Path.Combine(dir, "decision_groups.png"));
            }

            // 강제 효과를 올리면 체인 1 다음은 발동자(나) → 선택 효과 2개만 남은 창 (패스 가능)
            int mandatoryIndex = -1;
            for (int i = 0; i < session.State.Decision.Options.Count; i++)
            {
                var t = session.State.PendingTriggers.Find(p => p.Id == session.State.Decision.Options[i]);
                if (!t.IsOptional)
                    mandatoryIndex = i;
            }
            bootstrap.Decisions.ActivateOption(mandatoryIndex);
            started = Time.realtimeSinceStartup;
            while (!bootstrap.Decisions.IsOpen && Time.realtimeSinceStartup - started < 30f)
                yield return null;
            Assert.That(bootstrap.Decisions.MandatoryShown, Is.EqualTo(0));
            Assert.That(bootstrap.Decisions.OptionalShown, Is.EqualTo(2));
            Assert.That(session.State.Decision.CanPass, Is.True);
            if (dir != null)
            {
                yield return null;
                PresentationPlayModeTests.Capture(System.IO.Path.Combine(dir, "decision_optional_only.png"));
            }
            bootstrap.Decisions.Pass();
        }

        [UnityTest]
        public IEnumerator Panel_ManyEffects_Scrolls()
        {
            SceneManager.LoadScene("Battle");
            yield return null;
            yield return null;
            var bootstrap = Object.FindAnyObjectByType<BattleBootstrap>();
            bootstrap.Spectate = false;
            bootstrap.Speed = 10f;
            var session = bootstrap.Session;

            float started = Time.realtimeSinceStartup;
            while (!(session.HumanCanAct && session.State.Decision == null) && Time.realtimeSinceStartup - started < 60f)
                yield return null;
            Assert.That(session.HumanCanAct, Is.True);

            // 동료 4 + 기물 3 + 필드 1 = "카드 사용 시" 효과 8개 (강제 3, 선택 5) + 0코스트 주문
            var state = session.State;
            var me = state.GetPlayer(session.Human);
            foreach (var zone in new[] { me.Companions, me.Minions, me.Field })
            {
                while (zone.Count > 0)
                    zone.Remove(zone.Entities[0]);
            }
            for (int i = 0; i < 4; i++)
                me.Companions.TryAdd(state.CreateEntity(session.Human, Companion($"교관 {i + 1}", OnCardPlayedHit(1, optional: i % 2 == 0))));
            for (int i = 0; i < 3; i++)
            {
                var minion = new CardDefinition { Id = $"t_m{i}", Name = $"시험 기물 {i + 1}", Type = CardType.Minion, Cost = 1, Attack = 1, Health = 3 };
                minion.Effects.Add(OnCardPlayedHit(1, optional: i != 1));
                me.Minions.TryAdd(state.CreateEntity(session.Human, minion));
            }
            var field = new CardDefinition { Id = "t_f", Name = "시험 필드", Type = CardType.Field, Cost = 1 };
            field.Effects.Add(OnCardPlayedHit(1, optional: true));
            me.Field.TryAdd(state.CreateEntity(session.Human, field));
            var spell = state.CreateEntity(session.Human, new CardDefinition { Id = "t_s", Name = "시험 주문", Type = CardType.Spell, Cost = 0 });
            if (me.Hand.IsFull)
                me.Hand.Remove(me.Hand.Entities[0]);
            me.Hand.TryAdd(spell);
            bootstrap.Board.Render(state);

            Assert.That(session.TrySubmit(new PlayCardCommand(session.Human, spell.Id), out var reason), Is.True, reason);
            started = Time.realtimeSinceStartup;
            while (!bootstrap.Decisions.IsOpen && Time.realtimeSinceStartup - started < 30f)
                yield return null;

            Assert.That(bootstrap.Decisions.MandatoryShown + bootstrap.Decisions.OptionalShown, Is.EqualTo(8));
            Assert.That(bootstrap.Decisions.IsScrollable, Is.True, "8개면 스크롤");

            string dir = System.Environment.GetEnvironmentVariable("TCG_CAPTURE_DIR");
            if (dir != null)
            {
                yield return null;
                PresentationPlayModeTests.Capture(System.IO.Path.Combine(dir, "decision_scroll_top.png"));
                bootstrap.Decisions.ScrollTo(0f);
                yield return null;
                PresentationPlayModeTests.Capture(System.IO.Path.Combine(dir, "decision_scroll_bottom.png"));
            }
        }
    }
}
