using System.Linq;
using NUnit.Framework;
using TCG.Core.Data;
using TCG.Core.Events;

namespace TCG.Tests.Core
{
    public class EffectRuleTests
    {
        [Test]
        public void AuraField_BuffsFriendlyMinions_UntilReplaced()
        {
            var g = new TestGame();
            var minion = g.AddToBoard(0, TestCards.Minion(attack: 2, health: 2));
            var aura = g.AddToHand(0, TestCards.FieldWith("c_banner",
                TestCards.Effect(TriggerType.Aura, TargetType.AllFriendlyMinions, ActionType.ModifyAttack, 1)));

            g.Play(aura);
            Assert.That(minion.Attack, Is.EqualTo(3));
            Assert.That(g.Events<StatsChangedEvent>().Any(e => e.EntityId == minion.Id && e.Attack == 3), Is.True);

            var later = g.AddToHand(0, TestCards.Minion(cost: 0, attack: 1, health: 1));
            g.Play(later);
            Assert.That(later.Attack, Is.EqualTo(2), "나중에 나온 기물도 상시 효과를 받는다");

            g.Play(g.AddToHand(0, TestCards.FieldWith("c_plain")));
            Assert.That(minion.Attack, Is.EqualTo(2));
        }

        [Test]
        public void LosingHealthAura_CanKillDamagedMinion()
        {
            var g = new TestGame();
            g.AddToBoard(0, TestCards.FieldWith("c_hp",
                TestCards.Effect(TriggerType.Aura, TargetType.AllFriendlyMinions, ActionType.ModifyHealth, 2)));
            var minion = g.AddToBoard(0, TestCards.Minion(attack: 1, health: 1));
            minion.Damage = 2; // 1 + 2(상시) = 3 중 2 피해 → 남은 체력 1

            g.Play(g.AddToHand(0, TestCards.FieldWith("c_plain")));

            Assert.That(g.P0.Graveyard.Contains(minion), Is.True);
        }

        [Test]
        public void CostAura_ReducesHandCosts()
        {
            var g = new TestGame();
            var card = g.AddToHand(0, TestCards.Minion(cost: 3));
            var companion = g.AddToHand(0, TestCards.CompanionWith("c_discount", null,
                TestCards.Effect(TriggerType.Aura, TargetType.FriendlyHero, ActionType.ModifyCost, -1)));

            g.Play(companion);

            Assert.That(card.Cost, Is.EqualTo(2));
        }

        [Test]
        public void ConditionFalse_EffectDoesNotFire()
        {
            var g = new TestGame();
            for (int i = 0; i < 5; i++)
                g.AddToHand(0, TestCards.Minion());
            var condition = new ConditionDefinition { Type = ConditionType.HandCountAtMost, Value = 2 };
            // 낼 때 효과의 조건은 낼 수 있는지로 판정하므로(PlayCardTests), 보드에 있는 카드의 턴 종료 효과로 확인한다.
            g.AddToBoard(0, TestCards.CompanionWith("c_cond", null,
                TestCards.Effect(TriggerType.OnTurnEnd, TargetType.EnemyHero, ActionType.Damage, 3, conditions: condition)));
            g.FillDeck(1, 2);

            g.EndTurn();

            Assert.That(g.P1.Hero.Health, Is.EqualTo(30));
        }

        [Test]
        public void Heal_DoesNotExceedMaxHealth()
        {
            var g = new TestGame();
            g.P0.Hero.Health = 25;
            g.Play(g.AddToHand(0, TestCards.Spell("c_heal", 0,
                TestCards.Effect(TriggerType.OnEnter, TargetType.FriendlyHero, ActionType.Heal, 10))));

            Assert.That(g.P0.Hero.Health, Is.EqualTo(30));
            Assert.That(g.Events<HealedEvent>().Single().Amount, Is.EqualTo(5));
        }

        [Test]
        public void Discard_RemovesRandomCardsFromTargetPlayersHand()
        {
            var g = new TestGame();
            for (int i = 0; i < 4; i++)
                g.AddToHand(1, TestCards.Minion());
            g.Play(g.AddToHand(0, TestCards.Spell("c_discard", 0,
                TestCards.Effect(TriggerType.OnEnter, TargetType.EnemyHero, ActionType.Discard, 2))));

            Assert.That(g.P1.Hand.Count, Is.EqualTo(2));
            Assert.That(g.P1.Graveyard.Count, Is.EqualTo(2));
        }

        [Test]
        public void DamageToFieldOrCompanion_IsIgnored()
        {
            var g = new TestGame();
            var companion = g.AddToBoard(1, TestCards.Companion(2));
            g.Play(g.AddToHand(0, TestCards.Spell("c_bolt", 0,
                TestCards.Effect(TriggerType.OnEnter, TargetType.Chosen, ActionType.Damage, 5))), target: companion.Id);

            Assert.That(g.P1.Companions.Contains(companion), Is.True);
            Assert.That(g.Events<DamageDealtEvent>(), Is.Empty);
        }

        [Test]
        public void ModifyUsage_ToZero_RemovesCompanion()
        {
            var g = new TestGame();
            var companion = g.AddToBoard(1, TestCards.Companion(2));
            g.Play(g.AddToHand(0, TestCards.Spell("c_drain", 0,
                TestCards.Effect(TriggerType.OnEnter, TargetType.Chosen, ActionType.ModifyUsage, -2))), target: companion.Id);

            Assert.That(g.P1.Graveyard.Contains(companion), Is.True);
        }

        [Test]
        public void EndlessChain_StopsAtResolutionLimit()
        {
            var g = new TestGame(new RuleConfig { MaxTriggerResolutionsPerAction = 50 });
            var def = TestCards.Minion("c_echo", 0, 0, 1000);
            def.Effects.Add(TestCards.Effect(TriggerType.OnAnyDamageTaken, TargetType.AllFriendlyMinions, ActionType.Damage, 1));
            var a = g.AddToBoard(1, def);
            g.AddToBoard(1, def);

            g.Play(g.AddToHand(0, TestCards.Spell("c_poke", 0,
                TestCards.Effect(TriggerType.OnEnter, TargetType.Chosen, ActionType.Damage, 1))), target: a.Id);

            Assert.That(g.Events<ResolutionLimitReachedEvent>().Count(), Is.EqualTo(1));
            Assert.That(g.State.Phase, Is.EqualTo(TCG.Core.State.GamePhase.InProgress));
        }
    }
}
