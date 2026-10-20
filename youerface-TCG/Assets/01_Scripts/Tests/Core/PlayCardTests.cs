using System.Linq;
using NUnit.Framework;
using TCG.Core.Data;
using TCG.Core.Events;
using TCG.Core.State;

namespace TCG.Tests.Core
{
    public class PlayCardTests
    {
        [Test]
        public void PlayMinion_PaysCost_AndEntersBoard()
        {
            var g = new TestGame();
            var card = g.AddToHand(0, TestCards.Minion(cost: 3));

            g.Play(card);

            Assert.That(g.P0.AvailableCost, Is.EqualTo(7));
            Assert.That(g.P0.Minions.Contains(card), Is.True);
            Assert.That(g.P0.Hand.Contains(card), Is.False);
            Assert.That(g.Events<CardEnteredEvent>().Single().Zone, Is.EqualTo(ZoneType.Minion));
        }

        [Test]
        public void PlayMinion_AtChosenSlot_OthersDoNotMove()
        {
            var g = new TestGame();
            var a = g.AddToBoard(0, TestCards.Minion()); // 칸 0
            var b = g.AddToBoard(0, TestCards.Minion()); // 칸 1
            var card = g.AddToHand(0, TestCards.Minion());

            g.Play(card, position: 3);

            Assert.That(card.Slot, Is.EqualTo(3));
            Assert.That(new[] { a.Slot, b.Slot }, Is.EqualTo(new[] { 0, 1 }));
            Assert.That(g.P0.Minions.Entities, Is.EqualTo(new[] { a, b, card }), "목록은 칸 번호 순");
            Assert.That(g.Events<TCG.Core.Events.CardEnteredEvent>().Single().Slot, Is.EqualTo(3));
        }

        static ConditionDefinition Cond(ConditionType type, int value) => new ConditionDefinition { Type = type, Value = value };

        [Test]
        public void ConditionalCard_CanOnlyBePlayed_WhenConditionHolds()
        {
            var g = new TestGame();
            // 손패 1장 이하일 때: 상대 영웅에게 3 피해 (낸 카드는 손패에서 빠진 뒤로 판정)
            var spell = g.AddToHand(0, TestCards.Spell("c_last", 0,
                TestCards.Effect(TriggerType.OnEnter, TargetType.EnemyHero, ActionType.Damage, 3, null, null, Cond(ConditionType.HandCountAtMost, 1))));
            var a = g.AddToHand(0, TestCards.Minion());
            g.AddToHand(0, TestCards.Minion());

            var result = g.Validate(new TCG.Core.Commands.PlayCardCommand(0, spell.Id));
            Assert.That(result.IsValid, Is.False, "손패가 2장 남으므로 낼 수 없다");
            Assert.That(result.Reason, Does.Contain("조건"));

            g.P0.Hand.Remove(a);
            Assert.That(g.Validate(new TCG.Core.Commands.PlayCardCommand(0, spell.Id)).IsValid, Is.True, "손패가 1장 남으면 낼 수 있다");
            Assert.That(g.P0.Hand.Contains(spell), Is.True, "판정은 상태를 바꾸지 않는다");
        }

        [Test]
        public void ConditionalMinion_CountsItselfOnBoard_LikeWhenItTriggers()
        {
            var g = new TestGame();
            g.AddToBoard(0, TestCards.Minion());
            var def = TestCards.Minion("c_pack", 1, 1, 1);
            def.Effects.Add(TestCards.Effect(TriggerType.OnEnter, TargetType.EnemyHero, ActionType.Damage, 1, null, null,
                Cond(ConditionType.FriendlyMinionCountAtLeast, 2)));
            var card = g.AddToHand(0, def);

            Assert.That(g.Validate(new TCG.Core.Commands.PlayCardCommand(0, card.Id)).IsValid, Is.True);
            g.Play(card);
            Assert.That(g.P1.Hero.Health, Is.EqualTo(29));
        }

        [Test]
        public void ConditionOnNonPlayTrigger_DoesNotBlockPlaying()
        {
            var g = new TestGame();
            var def = TestCards.Minion("c_late", 1, 1, 1);
            def.Effects.Add(TestCards.Effect(TriggerType.OnTurnEnd, TargetType.EnemyHero, ActionType.Damage, 1, null, null,
                Cond(ConditionType.HandCountAtMost, 0)));
            var card = g.AddToHand(0, def);
            g.AddToHand(0, TestCards.Minion());

            Assert.That(g.Validate(new TCG.Core.Commands.PlayCardCommand(0, card.Id)).IsValid, Is.True);
        }

        [Test]
        public void PlayedEvent_CarriesChosenTarget_AndSurvivesCodec()
        {
            var g = new TestGame();
            var target = g.AddToBoard(1, TestCards.Minion("c_t", 1, 1, 9));
            g.Play(g.AddToHand(0, TestCards.Spell("c_bolt", 0,
                TestCards.Effect(TriggerType.OnEnter, TargetType.Chosen, ActionType.Damage, 1))), target: target.Id);

            var played = g.Events<CardPlayedEvent>().Single();
            Assert.That(played.TargetId, Is.EqualTo(target.Id));
            var back = (CardPlayedEvent)TCG.Core.Net.EventCodec.Decode(TCG.Core.Net.EventCodec.Encode(played));
            Assert.That(back.TargetId, Is.EqualTo(target.Id));
        }

        [Test]
        public void PlayMinion_OccupiedOrMissingSlot_IsRejected()
        {
            var g = new TestGame();
            g.AddToBoard(0, TestCards.Minion()); // 칸 0
            var card = g.AddToHand(0, TestCards.Minion());

            Assert.That(g.Validate(new TCG.Core.Commands.PlayCardCommand(0, card.Id, null, 0)).IsValid, Is.False, "차 있는 칸");
            Assert.That(g.Validate(new TCG.Core.Commands.PlayCardCommand(0, card.Id, null, 5)).IsValid, Is.False, "없는 칸");
            Assert.That(g.Validate(new TCG.Core.Commands.PlayCardCommand(0, card.Id, null, 4)).IsValid, Is.True);
        }

        [Test]
        public void PlayMinion_WithoutSlot_GoesNearestCenter()
        {
            var g = new TestGame();
            var first = g.AddToHand(0, TestCards.Minion());
            var second = g.AddToHand(0, TestCards.Minion());
            var third = g.AddToHand(0, TestCards.Minion());

            g.Play(first);
            g.Play(second);
            g.Play(third);

            Assert.That(new[] { first.Slot, second.Slot, third.Slot }, Is.EqualTo(new[] { 2, 1, 3 }), "가운데 → 왼쪽 → 오른쪽");
        }

        [Test]
        public void DeadMinion_LeavesGap_OthersStay()
        {
            var g = new TestGame();
            var a = g.AddToBoard(0, TestCards.Minion("c_a", 1, 1, 5));
            var b = g.AddToBoard(0, TestCards.Minion("c_b", 1, 1, 1));
            var c = g.AddToBoard(0, TestCards.Minion("c_c", 1, 1, 5));

            g.Apply(new TCG.Core.Commands.EndTurnCommand(0));
            g.Play(g.AddToHand(1, TestCards.Spell("c_bolt", 0,
                TestCards.Effect(TriggerType.OnEnter, TargetType.Chosen, ActionType.Damage, 1))), target: b.Id);

            Assert.That(b.Slot, Is.EqualTo(-1));
            Assert.That(new[] { a.Slot, c.Slot }, Is.EqualTo(new[] { 0, 2 }));
            Assert.That(g.P0.Minions.IsSlotFree(1), Is.True);
        }

        [Test]
        public void NotEnoughCost_IsRejected()
        {
            var g = new TestGame();
            g.P0.AvailableCost = 2;
            var card = g.AddToHand(0, TestCards.Minion(cost: 3));
            Assert.That(g.Validate(new TCG.Core.Commands.PlayCardCommand(0, card.Id)).IsValid, Is.False);
        }

        [Test]
        public void FullMinionZone_CannotPlayMinion()
        {
            var g = new TestGame();
            for (int i = 0; i < g.State.Rules.MinionSlots; i++)
                g.AddToBoard(0, TestCards.Minion());
            var card = g.AddToHand(0, TestCards.Minion());

            Assert.That(g.Validate(new TCG.Core.Commands.PlayCardCommand(0, card.Id)).IsValid, Is.False);
        }

        [Test]
        public void FullCompanionZone_CannotPlayCompanion()
        {
            var g = new TestGame();
            for (int i = 0; i < 4; i++)
                g.AddToBoard(0, TestCards.Companion(null));
            var card = g.AddToHand(0, TestCards.Companion(null));

            Assert.That(g.Validate(new TCG.Core.Commands.PlayCardCommand(0, card.Id)).IsValid, Is.False);
        }

        [Test]
        public void NewField_ReplacesOld_AndOldExitEffectFires()
        {
            var g = new TestGame();
            var oldField = g.AddToBoard(0, TestCards.FieldWith("c_old",
                TestCards.Effect(TriggerType.OnExit, TargetType.EnemyHero, ActionType.Damage, 2)));
            var newField = g.AddToHand(0, TestCards.FieldWith("c_new"));

            g.Play(newField);

            Assert.That(g.P0.Field.Entities, Is.EqualTo(new[] { newField }));
            Assert.That(g.P0.Graveyard.Contains(oldField), Is.True);
            Assert.That(g.Events<CardExitedEvent>().Single().Reason, Is.EqualTo(ExitReason.Replaced));
            Assert.That(g.P1.Hero.Health, Is.EqualTo(28));
        }

        [Test]
        public void Spell_WithChosenTarget_RequiresTarget_AndGoesToGraveyard()
        {
            var g = new TestGame();
            var enemy = g.AddToBoard(1, TestCards.Minion(attack: 1, health: 3));
            var spell = g.AddToHand(0, TestCards.Spell("c_bolt", 1,
                TestCards.Effect(TriggerType.OnEnter, TargetType.Chosen, ActionType.Damage, 2)));

            Assert.That(g.Validate(new TCG.Core.Commands.PlayCardCommand(0, spell.Id)).IsValid, Is.False);

            g.Play(spell, target: enemy.Id);

            Assert.That(enemy.Health, Is.EqualTo(1));
            Assert.That(g.P0.Graveyard.Contains(spell), Is.True);
            Assert.That(g.Events<SpellCastEvent>().Single().Destination, Is.EqualTo(ZoneType.Graveyard));
        }

        [Test]
        public void ExhaustSpell_GoesToExile()
        {
            var g = new TestGame();
            var def = TestCards.Spell("c_once", 0, TestCards.Effect(TriggerType.OnEnter, TargetType.EnemyHero, ActionType.Damage, 1));
            def.Keywords.Add(Keyword.Exhaust);
            var spell = g.AddToHand(0, def);

            g.Play(spell);

            Assert.That(g.P0.Exile.Contains(spell), Is.True);
            Assert.That(g.P0.Graveyard.Contains(spell), Is.False);
            Assert.That(g.P1.Hero.Health, Is.EqualTo(29));
        }

        [Test]
        public void Companion_UsageConsumedPerTrigger_ThenLeaves()
        {
            var g = new TestGame();
            g.FillDeck(0, 5);
            var companion = g.AddToBoard(0, TestCards.CompanionWith("c_helper", 2,
                TestCards.Effect(TriggerType.OnCardPlayed, TargetType.FriendlyHero, ActionType.Draw, 1)));

            g.Play(g.AddToHand(0, TestCards.Minion(cost: 0)));
            Assert.That(companion.Usage, Is.EqualTo(1));

            g.Play(g.AddToHand(0, TestCards.Minion(cost: 0)));
            Assert.That(g.P0.Companions.Contains(companion), Is.False);
            Assert.That(g.P0.Graveyard.Contains(companion), Is.True);
            Assert.That(g.Events<CardExitedEvent>().Single().Reason, Is.EqualTo(ExitReason.UsageDepleted));
        }

        [Test]
        public void PermanentCompanion_NeverLeaves()
        {
            var g = new TestGame();
            g.FillDeck(0, 5);
            var companion = g.AddToBoard(0, TestCards.CompanionWith("c_forever", null,
                TestCards.Effect(TriggerType.OnCardPlayed, TargetType.FriendlyHero, ActionType.Draw, 1)));

            for (int i = 0; i < 3; i++)
                g.Play(g.AddToHand(0, TestCards.Minion(cost: 0)));

            Assert.That(g.P0.Companions.Contains(companion), Is.True);
        }
    }
}
