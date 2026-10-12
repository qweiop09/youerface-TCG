using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TCG.Core.Commands;
using TCG.Core.Data;
using TCG.Core.Events;
using TCG.Core.State;

namespace TCG.Tests.Core
{
    /// <summary>유희왕식 체인 규칙 (GameDesign §7.2).</summary>
    public class ChainTests
    {
        static EffectDefinition HitEnemyHero(int amount, TriggerType trigger = TriggerType.OnEnter) =>
            TestCards.Effect(trigger, TargetType.EnemyHero, ActionType.Damage, amount);

        static List<int> ResolvedSources(IEnumerable<GameEvent> events) =>
            events.OfType<EffectResolvingEvent>().Select(e => e.SourceId).ToList();

        [Test]
        public void SingleMandatoryTrigger_ResolvesWithoutAsking()
        {
            var g = new TestGame { AutoDecide = false };
            g.Play(g.AddToHand(0, TestCards.Spell("c_hit", 0, HitEnemyHero(3))));

            Assert.That(g.State.Decision, Is.Null);
            Assert.That(g.P1.Hero.Health, Is.EqualTo(27));
            Assert.That(g.Events<EffectActivatedEvent>().Count(), Is.EqualTo(1));
            Assert.That(g.Events<ChainEndedEvent>().Count(), Is.EqualTo(1));
        }

        [Test]
        public void ActivationEventsComeBeforeResolution()
        {
            var g = new TestGame();
            g.Play(g.AddToHand(0, TestCards.Spell("c_two", 0, HitEnemyHero(1), HitEnemyHero(2))));

            var events = g.LastEvents;
            int lastActivated = events.FindLastIndex(e => e is EffectActivatedEvent);
            int firstResolving = events.FindIndex(e => e is EffectResolvingEvent);
            Assert.That(lastActivated, Is.LessThan(firstResolving));
        }

        [Test]
        public void SameTimeTriggers_PlayerChoosesOrder_LastPlacedResolvesFirst()
        {
            var g = new TestGame { AutoDecide = false };
            var a = g.AddToBoard(0, TestCards.CompanionWith("c_a", null,
                TestCards.Effect(TriggerType.OnCardPlayed, TargetType.EnemyHero, ActionType.Damage, 1)));
            var b = g.AddToBoard(0, TestCards.CompanionWith("c_b", null,
                TestCards.Effect(TriggerType.OnCardPlayed, TargetType.EnemyHero, ActionType.Damage, 2)));

            g.Play(g.AddToHand(0, TestCards.Minion(cost: 0)));

            var decision = g.State.Decision;
            Assert.That(decision.Type, Is.EqualTo(DecisionType.ChooseTriggers));
            Assert.That(decision.PlayerIndex, Is.EqualTo(0));
            Assert.That(decision.Options.Count, Is.EqualTo(2));
            Assert.That(g.Validate(new EndTurnCommand(0)).IsValid, Is.False, "선택 전에는 다른 행동 불가");

            int triggerA = g.State.PendingTriggers.Single(t => t.SourceId == a.Id).Id;
            int triggerB = g.State.PendingTriggers.Single(t => t.SourceId == b.Id).Id;
            var events = g.Apply(new ChooseTriggersCommand(0, new[] { triggerA, triggerB }));

            Assert.That(ResolvedSources(events), Is.EqualTo(new[] { b.Id, a.Id }));
            Assert.That(g.State.Decision, Is.Null);
        }

        [Test]
        public void TurnPlayerTriggersGoFirst_SoOpponentsResolveFirst()
        {
            var g = new TestGame();
            var attackerDef = TestCards.Minion("c_atk", 1, 1, 5);
            attackerDef.Effects.Add(TestCards.Effect(TriggerType.OnDamaged, TargetType.EnemyHero, ActionType.Damage, 1));
            var defenderDef = TestCards.Minion("c_def", 1, 1, 5);
            defenderDef.Effects.Add(TestCards.Effect(TriggerType.OnDamaged, TargetType.EnemyHero, ActionType.Damage, 1));
            var attacker = g.AddToBoard(0, attackerDef);
            var defender = g.AddToBoard(1, defenderDef);

            g.Attack(attacker, defender.Id);

            var activated = g.Events<EffectActivatedEvent>().ToList();
            Assert.That(activated.Select(e => e.SourceId), Is.EqualTo(new[] { attacker.Id, defender.Id }));
            Assert.That(ResolvedSources(g.LastEvents), Is.EqualTo(new[] { defender.Id, attacker.Id }));
        }

        [Test]
        public void OptionalTrigger_AsksEvenWhenAlone_AndCanBeDeclined()
        {
            var def = TestCards.Spell("c_maybe", 0, HitEnemyHero(5));
            def.Effects[0].Activation = EffectActivation.Optional;

            var g = new TestGame { AutoDecide = false };
            g.Play(g.AddToHand(0, def));
            Assert.That(g.State.Decision.Type, Is.EqualTo(DecisionType.ChooseTriggers));
            g.Apply(new ChooseTriggersCommand(0, new int[0]));
            Assert.That(g.P1.Hero.Health, Is.EqualTo(30));

            g.Play(g.AddToHand(0, def));
            g.Apply(new ChooseTriggersCommand(0, g.State.Decision.Options.ToList()));
            Assert.That(g.P1.Hero.Health, Is.EqualTo(25));
        }

        [Test]
        public void MandatoryTriggers_CannotBeLeftOut()
        {
            var g = new TestGame { AutoDecide = false };
            g.Play(g.AddToHand(0, TestCards.Spell("c_two", 0, HitEnemyHero(1), HitEnemyHero(2))));
            var options = g.State.Decision.Options;

            Assert.That(g.Validate(new ChooseTriggersCommand(0, new[] { options[0] })).IsValid, Is.False);
            Assert.That(g.Validate(new ChooseTriggersCommand(0, new[] { options[0], options[0] })).IsValid, Is.False);
            Assert.That(g.Validate(new ChooseTriggersCommand(1, options.ToList())).IsValid, Is.False, "다른 플레이어");
            Assert.That(g.Validate(new ChooseTriggersCommand(0, options.Reverse().ToList())).IsValid, Is.True);
        }

        [Test]
        public void DeathIsImmediate_AndDeathTriggersFormNextChain()
        {
            var g = new TestGame();
            g.FillDeck(1, 3);
            var chest = g.AddToBoard(1, TestCards.MinionWith("c_chest", 1, 0, 1, Keyword.TreasureChest));
            var other = g.AddToBoard(1, TestCards.Minion("c_other", 1, 1, 5));

            g.Play(g.AddToHand(0, TestCards.Spell("c_aoe", 0,
                TestCards.Effect(TriggerType.OnEnter, TargetType.AllEnemyMinions, ActionType.Damage, 2))));

            var events = g.LastEvents;
            int died = events.FindIndex(e => e is CardExitedEvent x && x.EntityId == chest.Id && x.Reason == ExitReason.Died);
            int otherHit = events.FindIndex(e => e is DamageDealtEvent d && d.TargetId == other.Id);
            int firstChainEnd = events.FindIndex(e => e is ChainEndedEvent);
            int chestActivated = events.FindIndex(e => e is EffectActivatedEvent a && a.SourceId == chest.Id);
            int drawn = events.FindIndex(e => e is CardDrawnEvent c && c.PlayerIndex == 1);

            Assert.That(died, Is.GreaterThan(-1));
            Assert.That(died, Is.LessThan(otherHit), "첫 대상이 죽은 뒤 다음 대상이 맞는다 (즉시 사망)");
            Assert.That(chestActivated, Is.GreaterThan(firstChainEnd), "보물상자는 다음 체인");
            Assert.That(drawn, Is.GreaterThan(chestActivated));
            Assert.That(g.Events<ChainEndedEvent>().Count(), Is.EqualTo(2));
        }

        [Test]
        public void NoResponseAvailable_AutoPasses()
        {
            var g = new TestGame { AutoDecide = false };
            g.Play(g.AddToHand(0, TestCards.Spell("c_hit", 0, HitEnemyHero(1))));

            Assert.That(g.Events<DecisionRequestedEvent>().Any(e => e.Type == DecisionType.ChainResponse), Is.False);
            Assert.That(g.Validate(new PassChainCommand(1)).IsValid, Is.False, "대기 중인 선택이 없음");
        }

        [Test]
        public void AttackTriggerChain_ResolvesBeforeCombat_EvenWithDecision()
        {
            var g = new TestGame { AutoDecide = false };
            var def = TestCards.Minion("c_striker", 1, 3, 5);
            def.Effects.Add(TestCards.Effect(TriggerType.OnAttack, TargetType.EnemyHero, ActionType.Damage, 1));
            def.Effects.Add(TestCards.Effect(TriggerType.OnAttack, TargetType.EnemyHero, ActionType.Damage, 1));
            var attacker = g.AddToBoard(0, def);
            var defender = g.AddToBoard(1, TestCards.Minion(attack: 1, health: 10));

            g.Attack(attacker, defender.Id);
            Assert.That(g.State.Decision, Is.Not.Null);
            Assert.That(defender.Damage, Is.EqualTo(0), "체인이 끝나기 전에는 전투 피해 없음");

            g.Apply(TestGame.DefaultAnswer(g.State.Decision));
            Assert.That(g.P1.Hero.Health, Is.EqualTo(28));
            Assert.That(defender.Damage, Is.EqualTo(3));
            Assert.That(attacker.Damage, Is.EqualTo(1));
        }

        [Test]
        public void TurnEnd_SummonsChain_ThenTurnPasses()
        {
            var g = new TestGame { AutoDecide = false };
            g.FillDeck(0, 5);
            g.FillDeck(1, 5);
            var spell = TestCards.SummonSpellFor(TestCards.Ember(3));
            g.Play(g.AddToHand(0, spell));
            g.Play(g.AddToHand(0, spell));

            g.EndTurn();
            Assert.That(g.State.Decision.Type, Is.EqualTo(DecisionType.ChooseTriggers), "소환물 2개 → 순서 선택");
            Assert.That(g.State.ActivePlayerIndex, Is.EqualTo(0), "체인이 끝나기 전에는 턴이 넘어가지 않음");

            g.Apply(TestGame.DefaultAnswer(g.State.Decision));
            Assert.That(g.State.ActivePlayerIndex, Is.EqualTo(1));
            Assert.That(g.P1.Hero.Health, Is.EqualTo(28));
            Assert.That(g.P0.Summons.Entities.All(e => e.Usage == 2), Is.True);
        }

        [Test]
        public void ChainState_IsPartOfStateHash()
        {
            var g = new TestGame { AutoDecide = false };
            g.Play(g.AddToHand(0, TestCards.Spell("c_two", 0, HitEnemyHero(1), HitEnemyHero(2))));
            ulong waiting = TCG.Core.Rules.StateHasher.Compute(g.State);

            g.Apply(TestGame.DefaultAnswer(g.State.Decision));
            Assert.That(TCG.Core.Rules.StateHasher.Compute(g.State), Is.Not.EqualTo(waiting));
        }
    }
}
