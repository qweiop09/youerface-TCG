using NUnit.Framework;
using TCG.Core.Data;
using TCG.Core.Scoring;

namespace TCG.Tests.Core
{
    /// <summary>
    /// 확정된 "방향"만 검증한다. 구체 점수값은 임시 가중치라 검증하지 않는다(Q7 논의 전).
    /// </summary>
    public class ScoreCalculatorTests
    {
        static readonly ScoreTable Table = ScoreTable.CreateTemporary();

        static int Score(CardDefinition card) => ScoreCalculator.Calculate(card, Table);

        [Test]
        public void SameCard_SameScore()
        {
            Assert.That(Score(TestCards.FireSpirit()), Is.EqualTo(Score(TestCards.FireSpirit())));
        }

        [Test]
        public void HigherAttackOrHealth_IncreasesScore()
        {
            int baseScore = Score(TestCards.Minion(attack: 3, health: 3));
            Assert.That(Score(TestCards.Minion(attack: 4, health: 3)), Is.GreaterThan(baseScore));
            Assert.That(Score(TestCards.Minion(attack: 3, health: 4)), Is.GreaterThan(baseScore));
        }

        [Test]
        public void HigherCost_DecreasesScore()
        {
            Assert.That(Score(TestCards.Minion(cost: 4)), Is.LessThan(Score(TestCards.Minion(cost: 3))));
        }

        [Test]
        public void AddingEffect_IncreasesScore()
        {
            var plain = TestCards.Minion();
            var withKeywordAndEffect = TestCards.FireSpirit();
            Assert.That(Score(withKeywordAndEffect), Is.GreaterThan(Score(plain)));
        }

        [Test]
        public void NestedSummonScore_IsAddedToParent()
        {
            int weak = Score(TestCards.SummonSpell(TestCards.Ember(usage: 1)));
            int strong = Score(TestCards.SummonSpell(TestCards.Ember(usage: 3)));
            Assert.That(strong, Is.GreaterThan(weak));
        }

        [Test]
        public void MinScore_ClampsWhenSet()
        {
            var table = new ScoreTable { MinScore = 0 };
            var expensive = TestCards.Minion(cost: 50, attack: 0, health: 1);

            Assert.That(ScoreCalculator.Calculate(expensive, Table), Is.LessThan(0));
            Assert.That(ScoreCalculator.Calculate(expensive, table), Is.EqualTo(0));
        }
    }
}
