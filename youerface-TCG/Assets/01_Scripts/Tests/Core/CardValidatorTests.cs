using NUnit.Framework;
using TCG.Core.Data;

namespace TCG.Tests.Core
{
    public class CardValidatorTests
    {
        [Test]
        public void ValidCards_HaveNoErrors()
        {
            Assert.That(CardValidator.Validate(TestCards.FireSpirit()), Is.Empty);
            Assert.That(CardValidator.Validate(TestCards.SummonSpell()), Is.Empty);
            Assert.That(CardValidator.Validate(TestCards.Field()), Is.Empty);
            Assert.That(CardValidator.Validate(TestCards.Companion(null)), Is.Empty);
            Assert.That(CardValidator.Validate(TestCards.Companion(3)), Is.Empty);
        }

        [Test]
        public void Minion_RequiresAttackAndHealth()
        {
            var card = TestCards.Minion();
            card.Health = null;
            Assert.That(CardValidator.Validate(card), Is.Not.Empty);

            card = TestCards.Minion(health: 0);
            Assert.That(CardValidator.Validate(card), Is.Not.Empty);
        }

        [Test]
        public void NonMinion_CannotHaveAttackOrHealth()
        {
            var field = TestCards.Field();
            field.Health = 5;
            Assert.That(CardValidator.Validate(field), Is.Not.Empty);
        }

        [Test]
        public void Summon_CannotBeTopLevelCard()
        {
            Assert.That(CardValidator.Validate(TestCards.Ember()), Is.Not.Empty);
        }

        [Test]
        public void Summon_RequiresUsage()
        {
            var spell = TestCards.SummonSpell(TestCards.Ember(usage: 0));
            Assert.That(CardValidator.Validate(spell), Is.Not.Empty);
        }

        [Test]
        public void Companion_UsageMustBePositiveIfSet()
        {
            Assert.That(CardValidator.Validate(TestCards.Companion(0)), Is.Not.Empty);
        }

        [Test]
        public void Usage_OnlyForSummonAndCompanion()
        {
            var minion = TestCards.Minion();
            minion.Usage = 2;
            Assert.That(CardValidator.Validate(minion), Is.Not.Empty);
        }

        [Test]
        public void CreateSummon_MustContainSummonDefinition()
        {
            var spell = TestCards.SummonSpell();
            spell.Effects[0].Action.Summon = null;
            Assert.That(CardValidator.Validate(spell), Is.Not.Empty);

            spell = TestCards.SummonSpell(TestCards.Minion());
            Assert.That(CardValidator.Validate(spell), Is.Not.Empty);
        }

        [Test]
        public void NegativeCost_IsInvalid()
        {
            Assert.That(CardValidator.Validate(TestCards.Minion(cost: -1)), Is.Not.Empty);
        }
    }
}
