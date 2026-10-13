using NUnit.Framework;
using TCG.Core.Data;
using TCG.Core.Scoring;

namespace TCG.Tests.Core
{
    /// <summary>점수 공식 v1 (GameDesign §6.1) 검증.</summary>
    public class ScoreCalculatorTests
    {
        static readonly ScoreTable Table = ScoreTable.CreateDefault();

        static int Score(CardDefinition card) => ScoreCalculator.Calculate(card, Table);

        static CardDefinition SpellOf(EffectDefinition effect, int cost = 0) => TestCards.Spell("c_s", cost, effect);

        [Test]
        public void SameCard_SameScore()
        {
            Assert.That(Score(TestCards.FireSpirit()), Is.EqualTo(Score(TestCards.FireSpirit())));
        }

        [Test]
        public void VanillaCurveMinion_ScoresZero()
        {
            Assert.That(Score(TestCards.Minion(cost: 1, attack: 1, health: 2)), Is.EqualTo(0));
            Assert.That(Score(TestCards.Minion(cost: 3, attack: 3, health: 4)), Is.EqualTo(0));
        }

        [Test]
        public void HigherAttackOrHealth_IncreasesScore_HigherCost_Decreases()
        {
            int baseScore = Score(TestCards.Minion(attack: 3, health: 3));
            Assert.That(Score(TestCards.Minion(attack: 4, health: 3)), Is.EqualTo(baseScore + 10));
            Assert.That(Score(TestCards.Minion(attack: 3, health: 4)), Is.EqualTo(baseScore + 10));
            Assert.That(Score(TestCards.Minion(cost: 4, attack: 3, health: 3)), Is.EqualTo(baseScore - 20));
        }

        [Test]
        public void FireSpirit_KeywordAndEffect()
        {
            // 3코스트 3/2(-20) + 도발 10 + 등장 시 무작위 적 기물 1에 2 피해 20
            Assert.That(Score(TestCards.FireSpirit()), Is.EqualTo(10));
        }

        [Test]
        public void HarmfulToEnemy_Positive_HarmfulToSelf_Penalty()
        {
            int toEnemy = Score(SpellOf(TestCards.Effect(TriggerType.OnEnter, TargetType.EnemyHero, ActionType.Damage, 2)));
            int toSelf = Score(SpellOf(TestCards.Effect(TriggerType.OnEnter, TargetType.FriendlyHero, ActionType.Damage, 2)));
            int healEnemy = Score(SpellOf(TestCards.Effect(TriggerType.OnEnter, TargetType.EnemyHero, ActionType.Heal, 2)));

            Assert.That(toEnemy, Is.EqualTo(-10 + 20));
            Assert.That(toSelf, Is.EqualTo(-10 - 20));
            Assert.That(healEnemy, Is.EqualTo(-10 - 10));
        }

        [Test]
        public void ChosenTarget_AlwaysPositive_WithMultiplier()
        {
            int damage = Score(SpellOf(TestCards.Effect(TriggerType.OnEnter, TargetType.Chosen, ActionType.Damage, 2)));
            int heal = Score(SpellOf(TestCards.Effect(TriggerType.OnEnter, TargetType.Chosen, ActionType.Heal, 2)));
            Assert.That(damage, Is.EqualTo(-10 + 26));
            Assert.That(heal, Is.EqualTo(-10 + 13));
        }

        [Test]
        public void TargetAndTriggerMultipliers()
        {
            int all = Score(SpellOf(TestCards.Effect(TriggerType.OnEnter, TargetType.AllEnemyMinions, ActionType.Damage, 1)));
            int random2 = Score(SpellOf(TestCards.Effect(TriggerType.OnEnter, TargetType.RandomEnemyMinion, ActionType.Damage, 1, count: 2)));
            var field = TestCards.FieldWith("c_f", TestCards.Effect(TriggerType.OnTurnEnd, TargetType.EnemyHero, ActionType.Damage, 1));

            Assert.That(all, Is.EqualTo(-10 + 25));
            Assert.That(random2, Is.EqualTo(-10 + 20));
            Assert.That(Score(field), Is.EqualTo(-10 - 20 + 20));
        }

        [Test]
        public void Destroy_IgnoresAmount()
        {
            int destroy = Score(SpellOf(TestCards.Effect(TriggerType.OnEnter, TargetType.Chosen, ActionType.Destroy, 5)));
            Assert.That(destroy, Is.EqualTo(-10 + 78));
        }

        [Test]
        public void Condition_ReducesEffect()
        {
            var plain = TestCards.Effect(TriggerType.OnEnter, TargetType.EnemyHero, ActionType.Damage, 10);
            var conditional = TestCards.Effect(TriggerType.OnEnter, TargetType.EnemyHero, ActionType.Damage, 10,
                conditions: new[] { new ConditionDefinition { Type = ConditionType.HandCountAtMost, Value = 2 } });
            Assert.That(Score(SpellOf(plain)), Is.EqualTo(-10 + 100));
            Assert.That(Score(SpellOf(conditional)), Is.EqualTo(-10 + 70));
        }

        [Test]
        public void Optional_BoostsBenefit_ZeroesPenalty()
        {
            var good = TestCards.Effect(TriggerType.OnEnter, TargetType.EnemyHero, ActionType.Damage, 10);
            good.Activation = EffectActivation.Optional;
            var bad = TestCards.Effect(TriggerType.OnEnter, TargetType.FriendlyHero, ActionType.Damage, 10);
            bad.Activation = EffectActivation.Optional;

            Assert.That(Score(SpellOf(good)), Is.EqualTo(-10 + 110));
            Assert.That(Score(SpellOf(bad)), Is.EqualTo(-10));
        }

        [Test]
        public void Penalties_CappedAtThirty_CostNotCounted()
        {
            var discard = TestCards.Effect(TriggerType.OnEnter, TargetType.FriendlyHero, ActionType.Discard, 5);
            var card = TestCards.Spell("c_p", 0, discard, discard);
            var result = ScoreCalculator.CalculateDetailed(card, Table);

            Assert.That(result.Total, Is.EqualTo(-10 - 30));
            Assert.That(Score(TestCards.Minion(cost: 10, attack: 0, health: 1)), Is.EqualTo(-10 + 10 - 200));
        }

        [Test]
        public void NestedSummon_UsesUsageAsRepeatMultiplier_Capped()
        {
            // 소환물: 기본·코스트 없음, 턴 종료 상대 영웅 1 피해 × 사용 횟수. 부모: 2코스트, 조건 1개(×0.7)
            Assert.That(Score(TestCards.SummonSpell(TestCards.Ember(usage: 1))), Is.EqualTo(-10 - 40 + 7));
            Assert.That(Score(TestCards.SummonSpell(TestCards.Ember(usage: 2))), Is.EqualTo(-10 - 40 + 14));
            Assert.That(Score(TestCards.SummonSpell(TestCards.Ember(usage: 5))), Is.EqualTo(-10 - 40 + 21));
        }

        [Test]
        public void Display_MinimumZero_TotalKeepsNegative()
        {
            var weak = TestCards.Minion(cost: 10, attack: 1, health: 1);
            var result = ScoreCalculator.CalculateDetailed(weak, Table);
            Assert.That(result.Total, Is.LessThan(0));
            Assert.That(result.Display, Is.EqualTo(0));
            Assert.That(ScoreCalculator.CalculateDisplay(TestCards.FireSpirit(), Table), Is.EqualTo(10));
        }

        [Test]
        public void Lines_SumToTotal()
        {
            var discard = TestCards.Effect(TriggerType.OnEnter, TargetType.FriendlyHero, ActionType.Discard, 5);
            var card = TestCards.Spell("c_p", 2, discard, TestCards.Effect(TriggerType.OnEnter, TargetType.EnemyHero, ActionType.Damage, 3));
            card.Keywords.Add(Keyword.Exhaust);
            var result = ScoreCalculator.CalculateDetailed(card, Table);

            int sum = 0;
            foreach (var line in result.Lines)
                sum += line.Value;
            Assert.That(sum, Is.EqualTo(result.Total));
            Assert.That(result.Lines.Exists(l => l.Kind == ScoreLineKind.PenaltyCap), Is.True);
        }
    }
}
