using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TCG.Core.Commands;
using TCG.Core.Data;
using TCG.Core.Rules;
using TCG.Core.State;

namespace TCG.Tests.Core
{
    /// <summary>
    /// 화면 없이 1판을 처음부터 끝까지 진행하고, 시드 + 명령 목록으로 다시 돌려 같은 상태가 되는지 확인한다.
    /// </summary>
    public class ReplayTests
    {
        static List<CardDefinition> MixedDeck()
        {
            var deck = new List<CardDefinition>();
            for (int i = 0; i < 6; i++)
            {
                deck.Add(TestCards.MinionWith("c_m" + i, 1 + i % 4, 1 + i % 3, 2 + i % 3));
                deck.Add(TestCards.MinionWith("c_t" + i, 2, 1, 4, Keyword.Taunt));
                deck.Add(TestCards.MinionWith("c_v" + i, 2, 2, 1, Keyword.Vanguard, Keyword.TreasureChest));
                deck.Add(TestCards.Spell("c_s" + i, 1,
                    TestCards.Effect(TriggerType.OnEnter, TargetType.RandomEnemyMinion, ActionType.Damage, 2, count: 1)));
                deck.Add(TestCards.SummonSpellFor(TestCards.Ember(2, i % 2 == 0 ? SummonDuplicateMode.AddNew : SummonDuplicateMode.RefreshUsage), "c_e" + i));
            }
            return deck;
        }

        /// <summary>단순 봇: 낼 수 있는 카드를 앞에서부터 내고, 가능한 공격을 다 한 뒤 턴을 넘긴다.</summary>
        static Command NextCommand(GameEngine engine, GameState state)
        {
            if (state.Decision != null)
            {
                // 순서 선택은 시드에 따라 뒤집어서 여러 순서를 섞어 본다.
                var d = state.Decision;
                if (d.Type == DecisionType.ChooseTriggers && state.TurnNumber % 2 == 0)
                    return new ChooseTriggersCommand(d.PlayerIndex, d.Options.Reverse().ToList());
                return TestGame.DefaultAnswer(d);
            }

            int p = state.ActivePlayerIndex;
            var player = state.GetPlayer(p);
            var opponent = state.GetOpponent(p);

            foreach (var card in player.Hand.Entities)
            {
                var play = new PlayCardCommand(p, card.Id, engine.RequiresChosenTarget(card) ? opponent.Hero.Id : (int?)null);
                if (engine.Validate(state, play).IsValid)
                    return play;
            }

            foreach (var attacker in player.Minions.Entities)
            {
                var targets = new List<int> { opponent.Hero.Id };
                targets.AddRange(opponent.Minions.Entities.Select(m => m.Id));
                foreach (int target in targets)
                {
                    var attack = new AttackCommand(p, attacker.Id, target);
                    if (engine.Validate(state, attack).IsValid)
                        return attack;
                }
            }

            return new EndTurnCommand(p);
        }

        static (List<Command> commands, List<ulong> hashes, GameState state) PlayOut(int seed)
        {
            var engine = new GameEngine();
            var state = new GameState(RuleConfig.CreateDefault(), seed);
            engine.StartGame(state, MixedDeck(), MixedDeck());

            var commands = new List<Command>();
            var hashes = new List<ulong> { StateHasher.Compute(state) };
            for (int step = 0; step < 5000 && state.Phase == GamePhase.InProgress; step++)
            {
                var command = NextCommand(engine, state);
                engine.Apply(state, command);
                commands.Add(command);
                hashes.Add(StateHasher.Compute(state));
            }
            return (commands, hashes, state);
        }

        [Test]
        public void FullGame_PlaysToTheEnd()
        {
            var (commands, _, state) = PlayOut(2026);

            Assert.That(state.Phase, Is.EqualTo(GamePhase.Finished));
            Assert.That(commands.Count, Is.GreaterThan(10));
        }

        [Test]
        public void Replay_SeedAndCommands_ReproduceSameHashes()
        {
            var (commands, hashes, _) = PlayOut(77);

            var engine = new GameEngine();
            var state = new GameState(RuleConfig.CreateDefault(), 77);
            engine.StartGame(state, MixedDeck(), MixedDeck());
            Assert.That(StateHasher.Compute(state), Is.EqualTo(hashes[0]));

            for (int i = 0; i < commands.Count; i++)
            {
                engine.Apply(state, commands[i]);
                Assert.That(StateHasher.Compute(state), Is.EqualTo(hashes[i + 1]), $"command {i}");
            }
        }

        [Test]
        public void DifferentSeeds_Diverge()
        {
            Assert.That(PlayOut(1).hashes.Last(), Is.Not.EqualTo(PlayOut(2).hashes.Last()));
        }
    }
}
