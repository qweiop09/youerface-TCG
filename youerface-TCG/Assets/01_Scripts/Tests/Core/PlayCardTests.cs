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
        public void PlayMinion_AtPosition()
        {
            var g = new TestGame();
            var a = g.AddToBoard(0, TestCards.Minion());
            var b = g.AddToBoard(0, TestCards.Minion());
            var card = g.AddToHand(0, TestCards.Minion());

            g.Play(card, position: 1);

            Assert.That(g.P0.Minions.Entities, Is.EqualTo(new[] { a, card, b }));
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
            for (int i = 0; i < 3; i++)
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
            Assert.That(g.Events<SpellFinishedEvent>().Single().Destination, Is.EqualTo(ZoneType.Graveyard));
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
