using System.Linq;
using NUnit.Framework;
using TCG.Core.Data;
using TCG.Core.Net;
using TCG.Core.State;

namespace TCG.Tests.Core
{
    /// <summary>능력치 변화 기록 (자세히 보기에서 출처·증감량 표시용).</summary>
    public class StatChangeTests
    {
        static CardDefinition BuffSpell(string id, int amount) => TestCards.Spell(id, 0,
            TestCards.Effect(TriggerType.OnEnter, TargetType.Chosen, ActionType.ModifyAttack, amount));

        [Test]
        public void PermanentChanges_AreMergedPerSource()
        {
            var g = new TestGame();
            var minion = g.AddToBoard(0, TestCards.Minion("c_m", 1, 2, 3));
            var spell = BuffSpell("c_buff", 2);

            g.Play(g.AddToHand(0, spell), target: minion.Id);
            g.Play(g.AddToHand(0, spell), target: minion.Id);
            g.Play(g.AddToHand(0, BuffSpell("c_curse", -1)), target: minion.Id);

            Assert.That(minion.Attack, Is.EqualTo(5));
            var changes = minion.Changes.Where(c => c.Stat == StatKind.Attack).ToList();
            Assert.That(changes.Count, Is.EqualTo(3), "카드 2장(같은 정의라도 다른 카드)과 저주 1장");
            Assert.That(changes.Sum(c => c.Amount), Is.EqualTo(3));
            Assert.That(changes.All(c => !c.Aura), Is.True);
            Assert.That(changes.Last().SourceName, Is.Not.Null);
        }

        [Test]
        public void SameSourceRepeated_IsMerged()
        {
            var g = new TestGame();
            var berserker = TestCards.Minion("c_berserk", 1, 1, 10);
            berserker.Effects.Add(TestCards.Effect(TriggerType.OnAnyDamageTaken, TargetType.Self, ActionType.ModifyAttack, 1));
            var m = g.AddToBoard(0, berserker);
            var bolt = TestCards.Spell("c_bolt", 0, TestCards.Effect(TriggerType.OnEnter, TargetType.Chosen, ActionType.Damage, 1));

            g.Play(g.AddToHand(0, bolt), target: m.Id);
            g.Play(g.AddToHand(0, bolt), target: m.Id);

            var change = m.Changes.Single();
            Assert.That(change.Amount, Is.EqualTo(2));
            Assert.That(change.SourceId, Is.EqualTo(m.Id));
        }

        [Test]
        public void AuraChanges_FollowTheAuraSource()
        {
            var g = new TestGame();
            var banner = g.AddToBoard(0, TestCards.FieldWith("c_banner",
                TestCards.Effect(TriggerType.Aura, TargetType.AllFriendlyMinions, ActionType.ModifyAttack, 1)));
            var m = g.AddToBoard(0, TestCards.Minion("c_m", 1, 2, 3));
            g.EndTurn(); // 처리 단계에서 상시 효과 계산
            g.FillDeck(0, 2);

            var aura = m.Changes.Single();
            Assert.That(aura.Aura, Is.True);
            Assert.That(aura.SourceId, Is.EqualTo(banner.Id));
            Assert.That(aura.Amount, Is.EqualTo(1));

            // 복제·스냅샷에도 남는다 (온라인 상대 화면).
            Assert.That(g.State.Clone().GetEntity(m.Id).Changes.Single().SourceId, Is.EqualTo(banner.Id));
            var catalog = new MatchCatalog();
            var restored = StateSnapshot.Capture(g.State, catalog).ToState(g.State.Rules, catalog.Cards);
            Assert.That(restored.GetEntity(m.Id).Changes.Single().Amount, Is.EqualTo(1));

            // 상시 효과의 출처가 사라지면 기록도 사라진다.
            g.Play(g.AddToHand(g.State.ActivePlayerIndex, TestCards.Spell("c_break", 0,
                TestCards.Effect(TriggerType.OnEnter, TargetType.Chosen, ActionType.Destroy))), target: banner.Id);
            Assert.That(m.Changes, Is.Empty);
        }
    }
}
