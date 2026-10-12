using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TCG.Core.Commands;
using TCG.Core.Data;
using TCG.Core.Events;
using TCG.Core.Rules;
using TCG.Core.State;

namespace TCG.Tests.Core
{
    public class GameFlowTests
    {
        static List<CardDefinition> Deck(int count)
        {
            var deck = new List<CardDefinition>();
            for (int i = 0; i < count; i++)
                deck.Add(TestCards.Minion("c_" + i, 1 + i % 5, 1 + i % 3, 1 + i % 4));
            return deck;
        }

        static GameState Started(int seed, out GameEngine engine, CardDefinition bonus = null)
        {
            var state = new GameState(RuleConfig.CreateDefault(), seed);
            engine = new GameEngine();
            engine.StartGame(state, Deck(30), Deck(30), bonus);
            return state;
        }

        [Test]
        public void StartGame_DealsStartingHandsAndStartsFirstTurn()
        {
            var bonus = TestCards.Spell("c_coin", 0);
            var state = Started(7, out _, bonus);
            int first = state.ActivePlayerIndex;
            var p1 = state.GetPlayer(first);
            var p2 = state.GetOpponent(first);

            Assert.That(state.Phase, Is.EqualTo(GamePhase.InProgress));
            Assert.That(state.TurnNumber, Is.EqualTo(1));
            Assert.That(p1.Hand.Count, Is.EqualTo(3 + 1), "선공 3장 + 첫 턴 드로우");
            Assert.That(p2.Hand.Count, Is.EqualTo(4 + 1), "후공 4장 + 보정 카드");
            Assert.That(p2.Hand.Entities.Any(e => e.Definition.Id == "c_coin"), Is.True);
            Assert.That(p1.Deck.Count, Is.EqualTo(30 - 4));
            Assert.That(p2.Deck.Count, Is.EqualTo(30 - 4));
            Assert.That(p1.MaxCost, Is.EqualTo(1));
            Assert.That(p1.AvailableCost, Is.EqualTo(1));
            Assert.That(p2.MaxCost, Is.EqualTo(0));
        }

        [Test]
        public void StartGame_SameSeed_SameState()
        {
            var a = Started(123, out _);
            var b = Started(123, out _);
            Assert.That(StateHasher.Compute(a), Is.EqualTo(StateHasher.Compute(b)));

            var c = Started(124, out _);
            Assert.That(StateHasher.Compute(c), Is.Not.EqualTo(StateHasher.Compute(a)));
        }

        [Test]
        public void EndTurn_SwitchesPlayer_AndMaxCostGrowsToLimit()
        {
            var state = Started(5, out var engine);
            int first = state.ActivePlayerIndex;

            engine.Apply(state, new EndTurnCommand(first));
            Assert.That(state.ActivePlayerIndex, Is.EqualTo(1 - first));
            Assert.That(state.TurnNumber, Is.EqualTo(2));
            Assert.That(state.ActivePlayer.MaxCost, Is.EqualTo(1));

            for (int i = 0; i < 30; i++)
                engine.Apply(state, new EndTurnCommand(state.ActivePlayerIndex));

            Assert.That(state.GetPlayer(0).MaxCost, Is.EqualTo(10));
            Assert.That(state.GetPlayer(1).MaxCost, Is.EqualTo(10));
        }

        [Test]
        public void Commands_FromInactivePlayer_AreRejected()
        {
            var state = Started(5, out var engine);
            int other = 1 - state.ActivePlayerIndex;
            Assert.That(engine.Validate(state, new EndTurnCommand(other)).IsValid, Is.False);
            Assert.Throws<System.InvalidOperationException>(() => engine.Apply(state, new EndTurnCommand(other)));
        }

        [Test]
        public void Draw_FromEmptyDeck_DealsIncreasingFatigue()
        {
            var g = new TestGame();
            g.EndTurn(); // 플레이어 1 턴 시작: 덱 없음 → 피로 1
            g.EndTurn(); // 플레이어 0 턴 시작: 피로 1
            g.EndTurn(); // 플레이어 1: 피로 2

            Assert.That(g.P1.FatigueCount, Is.EqualTo(2));
            Assert.That(g.P1.Hero.Health, Is.EqualTo(30 - 1 - 2));
            Assert.That(g.Events<FatigueEvent>().Single().Damage, Is.EqualTo(2));
        }

        [Test]
        public void Draw_WithFullHand_BurnsCard()
        {
            var g = new TestGame();
            for (int i = 0; i < 10; i++)
                g.AddToHand(1, TestCards.Minion());
            var top = g.AddToDeck(1, TestCards.Minion("c_top"));

            g.EndTurn();

            Assert.That(g.P1.Hand.Count, Is.EqualTo(10));
            Assert.That(g.P1.Exile.Contains(top), Is.True);
            Assert.That(g.Events<CardBurnedEvent>().Single().EntityId, Is.EqualTo(top.Id));
        }

        [Test]
        public void HeroDeath_EndsGame_AndBlocksFurtherCommands()
        {
            var g = new TestGame();
            g.P1.Hero.Health = 3;
            var minion = g.AddToBoard(0, TestCards.Minion(attack: 5, health: 5));

            g.Attack(minion, g.P1.Hero.Id);

            Assert.That(g.State.Phase, Is.EqualTo(GamePhase.Finished));
            Assert.That(g.State.WinnerIndex, Is.EqualTo(0));
            Assert.That(g.Events<GameEndedEvent>().Single().WinnerIndex, Is.EqualTo(0));
            Assert.That(g.Validate(new EndTurnCommand(0)).IsValid, Is.False);
        }

        [Test]
        public void BothHeroesDie_IsDraw()
        {
            var g = new TestGame();
            g.FillDeck(1, 3);
            g.P0.Hero.Health = 0;
            g.P1.Hero.Health = 0;

            g.EndTurn();

            Assert.That(g.State.Phase, Is.EqualTo(GamePhase.Finished));
            Assert.That(g.State.WinnerIndex, Is.Null);
        }

        [Test]
        public void HeroDeath_EndsGameImmediately_RemainingChainIsDropped()
        {
            var g = new TestGame { AutoDecide = false };
            g.P0.Hero.Health = 2;
            g.P1.Hero.Health = 2;
            var spell = g.AddToHand(0, TestCards.Spell("c_boom", 0,
                TestCards.Effect(TriggerType.OnEnter, TargetType.FriendlyHero, ActionType.Damage, 5),
                TestCards.Effect(TriggerType.OnEnter, TargetType.EnemyHero, ActionType.Damage, 5)));

            g.Play(spell);
            var options = g.State.Decision.Options;
            // 체인 1 = 상대 영웅 피해(내가 고름), 체인 2 = 내 영웅 피해(남은 강제 효과 자동) → 체인 2가 먼저 처리되어 내가 진다.
            g.Apply(new ChainActivateCommand(0, options[1]));

            Assert.That(g.State.Phase, Is.EqualTo(GamePhase.Finished));
            Assert.That(g.State.WinnerIndex, Is.EqualTo(1));
            Assert.That(g.P1.Hero.Health, Is.EqualTo(2));
        }
    }
}
