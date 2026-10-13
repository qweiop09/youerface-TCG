using NUnit.Framework;
using TCG.Core.Data;

namespace TCG.Tests.Core
{
    /// <summary>카드 제작기 편집 도우미: 기본값과 종류·행동 변경 시 정리.</summary>
    public class CardEditingTests
    {
        [Test]
        public void NewCard_IsValidMinion()
        {
            var card = CardEditing.NewCard("c_new");
            Assert.That(card.Type, Is.EqualTo(CardType.Minion));
            Assert.That(CardValidator.Validate(card), Is.Empty);

            card.Effects.Add(CardEditing.NewEffect());
            Assert.That(CardValidator.Validate(card), Is.Empty);
        }

        [Test]
        public void SetType_ClearsFieldsOfOtherTypes_AndDisallowedKeywords()
        {
            var card = CardEditing.NewCard("c_new");
            card.Keywords.Add(Keyword.Taunt);

            CardEditing.SetType(card, CardType.Spell);
            Assert.That(card.Attack, Is.Null);
            Assert.That(card.Health, Is.Null);
            Assert.That(card.Keywords, Is.Empty);
            Assert.That(CardValidator.Validate(card), Is.Empty);

            CardEditing.SetType(card, CardType.Companion);
            card.Usage = 3;
            CardEditing.SetType(card, CardType.Field);
            Assert.That(card.Usage, Is.Null);

            CardEditing.SetType(card, CardType.Minion);
            Assert.That(card.Attack, Is.EqualTo(1));
            Assert.That(card.Health, Is.EqualTo(1));
        }

        [Test]
        public void SetAction_CreateSummon_AddsValidSummon_OtherActionRemovesIt()
        {
            var card = CardEditing.NewCard("c_new", CardType.Spell);
            var effect = CardEditing.NewEffect();
            card.Effects.Add(effect);

            CardEditing.SetAction(effect, ActionType.CreateSummon);
            Assert.That(effect.Action.Summon, Is.Not.Null);
            Assert.That(effect.Action.Amount, Is.Null);
            Assert.That(CardValidator.Validate(card), Is.Empty);

            CardEditing.SetAction(effect, ActionType.Heal);
            Assert.That(effect.Action.Summon, Is.Null);
            Assert.That(effect.Action.Amount, Is.EqualTo(1));

            CardEditing.SetAction(effect, ActionType.Destroy);
            Assert.That(effect.Action.Amount, Is.Null);
        }

        [Test]
        public void SetTarget_RandomGetsCount_OthersNot()
        {
            var effect = CardEditing.NewEffect();
            CardEditing.SetTarget(effect, TargetType.RandomEnemyMinion);
            Assert.That(effect.Target.Count, Is.EqualTo(1));
            CardEditing.SetTarget(effect, TargetType.EnemyHero);
            Assert.That(effect.Target.Count, Is.Null);
        }

        [Test]
        public void Clone_IsDeep()
        {
            var card = TestCards.SummonSpell();
            var copy = CardEditing.Clone(card);
            copy.Effects[0].Action.Summon.Name = "바뀜";
            Assert.That(card.Effects[0].Action.Summon.Name, Is.Not.EqualTo("바뀜"));
        }

        [Test]
        public void Move_SwapsWithinBounds()
        {
            var list = new System.Collections.Generic.List<int> { 1, 2, 3 };
            CardEditing.Move(list, 0, 1);
            Assert.That(list, Is.EqualTo(new[] { 2, 1, 3 }));
            CardEditing.Move(list, 0, -1);
            Assert.That(list, Is.EqualTo(new[] { 2, 1, 3 }));
        }
    }
}
