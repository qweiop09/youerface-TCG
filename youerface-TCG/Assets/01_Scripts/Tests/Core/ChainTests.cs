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
        public void SameTimeTriggers_OneAtATime_PlayerChoosesWhichFirst()
        {
            var g = new TestGame { AutoDecide = false };
            var a = g.AddToBoard(0, TestCards.CompanionWith("c_a", null,
                TestCards.Effect(TriggerType.OnCardPlayed, TargetType.EnemyHero, ActionType.Damage, 1)));
            var b = g.AddToBoard(0, TestCards.CompanionWith("c_b", null,
                TestCards.Effect(TriggerType.OnCardPlayed, TargetType.EnemyHero, ActionType.Damage, 2)));

            g.Play(g.AddToHand(0, TestCards.Minion(cost: 0)));

            var decision = g.State.Decision;
            Assert.That(decision.Type, Is.EqualTo(DecisionType.ChainPriority));
            Assert.That(decision.PlayerIndex, Is.EqualTo(0));
            Assert.That(decision.Options.Count, Is.EqualTo(2));
            Assert.That(decision.CanPass, Is.False, "강제 효과가 남아 있으면 패스 불가");
            Assert.That(g.Validate(new EndTurnCommand(0)).IsValid, Is.False, "선택 전에는 다른 행동 불가");

            // A를 체인 1로 올리면, 남은 강제 효과 B는 내 체인 기회에 자동으로 체인 2가 된다.
            int triggerA = g.State.PendingTriggers.Single(t => t.SourceId == a.Id).Id;
            var events = g.Apply(new ChainActivateCommand(0, triggerA));

            Assert.That(g.Events<EffectActivatedEvent>().Select(e => e.SourceId), Is.EqualTo(new[] { a.Id, b.Id }));
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
            Assert.That(g.State.Decision.Type, Is.EqualTo(DecisionType.ChainPriority));
            Assert.That(g.State.Decision.CanPass, Is.True);
            g.Apply(new PassChainCommand(0));
            Assert.That(g.P1.Hero.Health, Is.EqualTo(30));
            Assert.That(g.State.PendingTriggers, Is.Empty, "체인이 닫히면 남은 선택 효과는 발동하지 않음");

            g.Play(g.AddToHand(0, def));
            g.Apply(new ChainActivateCommand(0, g.State.Decision.Options[0]));
            Assert.That(g.P1.Hero.Health, Is.EqualTo(25));
        }

        [Test]
        public void MandatoryTriggers_CannotPass_OnlyOfferedEffectsCanBePlaced()
        {
            var g = new TestGame { AutoDecide = false };
            g.Play(g.AddToHand(0, TestCards.Spell("c_two", 0, HitEnemyHero(1), HitEnemyHero(2))));
            var options = g.State.Decision.Options;

            Assert.That(g.Validate(new PassChainCommand(0)).IsValid, Is.False, "강제 효과가 남아 있음");
            Assert.That(g.Validate(new ChainActivateCommand(0, 9999)).IsValid, Is.False, "선택지에 없는 효과");
            Assert.That(g.Validate(new ChainActivateCommand(1, options[0])).IsValid, Is.False, "다른 플레이어");
            Assert.That(g.Validate(new ChainActivateCommand(0, options[1])).IsValid, Is.True);
        }

        /// <summary>
        /// 체인 1 다음은 발동자부터, 체인 2 이후 다음은 상대부터 체인 기회 (GameDesign §7.2 규칙 3).
        /// 공격자(나)에게 피격 시 강제 2개 + 선택 1개, 방어자(상대)에게 피격 시 강제 1개:
        /// 나 체인1 → 나(발동자 먼저) 체인2 → 상대(상대 먼저) 체인3 → 나 체인4(선택) → 둘 다 패스.
        /// </summary>
        [Test]
        public void Priority_AfterFirstLinkActivatorFirst_AfterChainOpponentFirst()
        {
            var g = new TestGame { AutoDecide = false };
            var attackerDef = TestCards.Minion("c_atk", 1, 1, 10);
            attackerDef.Effects.Add(TestCards.Effect(TriggerType.OnDamaged, TargetType.EnemyHero, ActionType.Damage, 1));
            attackerDef.Effects.Add(TestCards.Effect(TriggerType.OnDamaged, TargetType.EnemyHero, ActionType.Damage, 2));
            var optional = TestCards.Effect(TriggerType.OnDamaged, TargetType.EnemyHero, ActionType.Damage, 3);
            optional.Activation = EffectActivation.Optional;
            attackerDef.Effects.Add(optional);
            var defenderDef = TestCards.Minion("c_def", 1, 1, 10);
            defenderDef.Effects.Add(TestCards.Effect(TriggerType.OnDamaged, TargetType.EnemyHero, ActionType.Damage, 4));
            var attacker = g.AddToBoard(0, attackerDef);
            var defender = g.AddToBoard(1, defenderDef);

            var log = new List<GameEvent>(g.Attack(attacker, defender.Id));
            Assert.That(g.State.Decision.PlayerIndex, Is.EqualTo(0), "턴 플레이어가 먼저");
            Assert.That(g.State.Decision.Options.Count, Is.EqualTo(3));

            var first = g.State.PendingTriggers.First(t => t.SourceId == attacker.Id && t.EffectIndex == 0).Id;
            log.AddRange(g.Apply(new ChainActivateCommand(0, first)));
            // 체인 1 다음: 발동자(나) 먼저 → 강제 1개 + 선택 1개 남음 → 선택 필요
            Assert.That(g.State.Decision.PlayerIndex, Is.EqualTo(0));
            var second = g.State.PendingTriggers.First(t => t.SourceId == attacker.Id && t.EffectIndex == 1).Id;
            log.AddRange(g.Apply(new ChainActivateCommand(0, second)));
            // 체인 2 다음: 상대 먼저 → 상대 강제 1개 자동(체인 3) → 그다음 상대의 상대인 나 → 선택 효과만 남음
            Assert.That(g.State.Decision.PlayerIndex, Is.EqualTo(0));
            Assert.That(g.State.Decision.CanPass, Is.True);
            log.AddRange(g.Apply(new ChainActivateCommand(0, g.State.Decision.Options[0])));

            var activated = log.OfType<EffectActivatedEvent>().Select(e => (e.SourceId, e.EffectIndex)).ToList();
            Assert.That(activated, Is.EqualTo(new[] { (attacker.Id, 0), (attacker.Id, 1), (defender.Id, 0), (attacker.Id, 2) }));
            var resolved = log.OfType<EffectResolvingEvent>().Select(e => (e.SourceId, e.EffectIndex)).ToList();
            Assert.That(resolved, Is.EqualTo(new[] { (attacker.Id, 2), (defender.Id, 0), (attacker.Id, 1), (attacker.Id, 0) }));
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

            Assert.That(g.Events<DecisionRequestedEvent>().Any(), Is.False);
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
            Assert.That(g.State.Decision.Type, Is.EqualTo(DecisionType.ChainPriority), "소환물 2개 → 어느 것부터 올릴지 선택");
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
