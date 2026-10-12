using System.Linq;
using NUnit.Framework;
using TCG.Core.Commands;
using TCG.Core.Data;
using TCG.Core.Events;

namespace TCG.Tests.Core
{
    public class CombatTests
    {
        [Test]
        public void MinionsDamageEachOther_AndDeadGoToGraveyard()
        {
            var g = new TestGame();
            var attacker = g.AddToBoard(0, TestCards.Minion(attack: 3, health: 4));
            var defender = g.AddToBoard(1, TestCards.Minion(attack: 2, health: 3));

            g.Attack(attacker, defender.Id);

            Assert.That(attacker.Health, Is.EqualTo(2));
            Assert.That(g.P1.Graveyard.Contains(defender), Is.True);
            Assert.That(g.Events<CardExitedEvent>().Single().Reason, Is.EqualTo(ExitReason.Died));
        }

        [Test]
        public void AttackHero_HeroDoesNotStrikeBack()
        {
            var g = new TestGame();
            var attacker = g.AddToBoard(0, TestCards.Minion(attack: 3, health: 1));

            g.Attack(attacker, g.P1.Hero.Id);

            Assert.That(g.P1.Hero.Health, Is.EqualTo(27));
            Assert.That(attacker.Health, Is.EqualTo(1));
        }

        [Test]
        public void Minion_CannotAttackTwice_OrOnPlayedTurn()
        {
            var g = new TestGame();
            var attacker = g.AddToBoard(0, TestCards.Minion(attack: 1, health: 5));
            g.Attack(attacker, g.P1.Hero.Id);
            Assert.That(g.Validate(new AttackCommand(0, attacker.Id, g.P1.Hero.Id)).IsValid, Is.False);

            var fresh = g.AddToHand(0, TestCards.Minion(cost: 0));
            g.Play(fresh);
            Assert.That(g.Validate(new AttackCommand(0, fresh.Id, g.P1.Hero.Id)).IsValid, Is.False);

            g.EndTurn();
            g.EndTurn();
            Assert.That(g.Validate(new AttackCommand(0, fresh.Id, g.P1.Hero.Id)).IsValid, Is.True);
            Assert.That(g.Validate(new AttackCommand(0, attacker.Id, g.P1.Hero.Id)).IsValid, Is.True);
        }

        [Test]
        public void Taunt_MustBeAttackedFirst_AnyTauntCanBeChosen()
        {
            var g = new TestGame();
            var attacker = g.AddToBoard(0, TestCards.Minion(attack: 1, health: 5));
            var plain = g.AddToBoard(1, TestCards.Minion());
            var tauntA = g.AddToBoard(1, TestCards.MinionWith("c_ta", 1, 1, 5, Keyword.Taunt));
            var tauntB = g.AddToBoard(1, TestCards.MinionWith("c_tb", 1, 1, 5, Keyword.Taunt));

            Assert.That(g.Validate(new AttackCommand(0, attacker.Id, g.P1.Hero.Id)).IsValid, Is.False);
            Assert.That(g.Validate(new AttackCommand(0, attacker.Id, plain.Id)).IsValid, Is.False);
            Assert.That(g.Validate(new AttackCommand(0, attacker.Id, tauntA.Id)).IsValid, Is.True);
            Assert.That(g.Validate(new AttackCommand(0, attacker.Id, tauntB.Id)).IsValid, Is.True);
        }

        [Test]
        public void Stealth_CannotBeAttacked_ButCanBeEffectTarget()
        {
            var g = new TestGame();
            var attacker = g.AddToBoard(0, TestCards.Minion(attack: 1, health: 5));
            var hidden = g.AddToBoard(1, TestCards.MinionWith("c_hidden", 1, 1, 3, Keyword.Stealth));

            Assert.That(g.Validate(new AttackCommand(0, attacker.Id, hidden.Id)).IsValid, Is.False);

            var bolt = g.AddToHand(0, TestCards.Spell("c_bolt", 0,
                TestCards.Effect(TriggerType.OnEnter, TargetType.Chosen, ActionType.Damage, 1)));
            g.Play(bolt, target: hidden.Id);
            Assert.That(hidden.Health, Is.EqualTo(2));

            var random = g.AddToHand(0, TestCards.Spell("c_random", 0,
                TestCards.Effect(TriggerType.OnEnter, TargetType.RandomEnemyMinion, ActionType.Damage, 1, count: 1)));
            g.Play(random);
            Assert.That(hidden.Health, Is.EqualTo(1));
        }

        [Test]
        public void StealthTaunt_DoesNotForceAttacks()
        {
            var g = new TestGame();
            var attacker = g.AddToBoard(0, TestCards.Minion(attack: 1, health: 5));
            g.AddToBoard(1, TestCards.MinionWith("c_both", 1, 1, 3, Keyword.Taunt, Keyword.Stealth));

            Assert.That(g.Validate(new AttackCommand(0, attacker.Id, g.P1.Hero.Id)).IsValid, Is.True);
        }

        [Test]
        public void Stealth_StaysAfterAttacking()
        {
            var g = new TestGame();
            var hidden = g.AddToBoard(0, TestCards.MinionWith("c_hidden", 1, 2, 3, Keyword.Stealth));

            g.Attack(hidden, g.P1.Hero.Id);

            Assert.That(hidden.HasKeyword(Keyword.Stealth), Is.True);
        }

        [Test]
        public void Vanguard_DrawsOnEnter()
        {
            var g = new TestGame();
            g.FillDeck(0, 3);
            var card = g.AddToHand(0, TestCards.MinionWith("c_vanguard", 0, 1, 1, Keyword.Vanguard));

            g.Play(card);

            Assert.That(g.P0.Hand.Count, Is.EqualTo(1));
            Assert.That(g.Events<CardDrawnEvent>().Single().PlayerIndex, Is.EqualTo(0));
        }

        [Test]
        public void TreasureChest_OwnerDrawsOnDeath()
        {
            var g = new TestGame();
            g.FillDeck(1, 3);
            var attacker = g.AddToBoard(0, TestCards.Minion(attack: 5, health: 5));
            var chest = g.AddToBoard(1, TestCards.MinionWith("c_chest", 1, 0, 1, Keyword.TreasureChest));

            g.Attack(attacker, chest.Id);

            Assert.That(g.P1.Graveyard.Contains(chest), Is.True);
            Assert.That(g.P1.Hand.Count, Is.EqualTo(1));
            Assert.That(g.P0.Hand.Count, Is.EqualTo(0));
        }

        [Test]
        public void TreasureChest_AlsoDrawsWhenDestroyedByEffect()
        {
            var g = new TestGame();
            g.FillDeck(1, 3);
            var chest = g.AddToBoard(1, TestCards.MinionWith("c_chest", 1, 0, 5, Keyword.TreasureChest));
            var destroy = g.AddToHand(0, TestCards.Spell("c_kill", 0,
                TestCards.Effect(TriggerType.OnEnter, TargetType.Chosen, ActionType.Destroy)));

            g.Play(destroy, target: chest.Id);

            Assert.That(g.P1.Hand.Count, Is.EqualTo(1));
        }

        [Test]
        public void OnAttack_ResolvesBeforeCombat()
        {
            var g = new TestGame();
            var def = TestCards.Minion("c_striker", 1, 1, 5);
            def.Effects.Add(TestCards.Effect(TriggerType.OnAttack, TargetType.AllEnemyMinions, ActionType.Damage, 1));
            var attacker = g.AddToBoard(0, def);
            var defender = g.AddToBoard(1, TestCards.Minion(attack: 3, health: 1));

            g.Attack(attacker, defender.Id);

            Assert.That(g.P1.Graveyard.Contains(defender), Is.True);
            Assert.That(attacker.Health, Is.EqualTo(5), "대상이 공격 시 효과로 먼저 죽어 반격이 없다");
        }
    }
}
