using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TCG.Core.AI;
using TCG.Core.Commands;
using TCG.Core.Data;
using TCG.Core.Random;
using TCG.Core.Rules;
using TCG.Core.State;

namespace TCG.Tests.Core
{
    public class AiTests
    {
        static GameState NewGame(int seed, GameEngine engine)
        {
            var state = new GameState(RuleConfig.CreateDefault(), seed);
            engine.StartGame(state, ReplayTests.MixedDeck(), ReplayTests.MixedDeck());
            return state;
        }

        /// <summary>두 AI가 끝까지 대전. 반환: 승자(무승부·제한 초과는 null).</summary>
        static int? RunMatch(AiDifficulty a, AiDifficulty b, int seed, out int steps)
        {
            var engine = new GameEngine();
            var state = NewGame(seed, engine);
            var players = new[] { AiPlayers.Create(a, seed * 31 + 1, engine), AiPlayers.Create(b, seed * 31 + 2, engine) };

            for (steps = 0; steps < 3000 && state.Phase == GamePhase.InProgress; steps++)
            {
                int mover = state.Decision?.PlayerIndex ?? state.ActivePlayerIndex;
                var command = players[mover].ChooseCommand(state, mover);
                Assert.That(engine.Validate(state, command).IsValid, Is.True, $"AI {mover} chose invalid {command}");
                engine.Apply(state, command);
            }
            Assert.That(state.Phase, Is.EqualTo(GamePhase.Finished), "game did not finish");
            return state.WinnerIndex;
        }

        [Test]
        public void Clone_HasSameHash_AndIsIndependent()
        {
            var engine = new GameEngine();
            var state = NewGame(3, engine);
            var clone = state.Clone();
            ulong before = StateHasher.Compute(state);

            Assert.That(StateHasher.Compute(clone), Is.EqualTo(before));

            engine.Apply(clone, new EndTurnCommand(clone.ActivePlayerIndex));
            Assert.That(StateHasher.Compute(state), Is.EqualTo(before), "원본은 그대로");
            Assert.That(StateHasher.Compute(clone), Is.Not.EqualTo(before));
        }

        [Test]
        public void Clone_ContinuesIdentically()
        {
            var engine = new GameEngine();
            var state = NewGame(4, engine);
            var clone = state.Clone();
            var ai1 = AiPlayers.Create(AiDifficulty.Easy, 9, engine);
            var ai2 = AiPlayers.Create(AiDifficulty.Easy, 9, engine);

            for (int i = 0; i < 40 && state.Phase == GamePhase.InProgress; i++)
            {
                int mover = state.Decision?.PlayerIndex ?? state.ActivePlayerIndex;
                var c1 = ai1.ChooseCommand(state, mover);
                var c2 = ai2.ChooseCommand(clone, mover);
                engine.Apply(state, c1);
                engine.Apply(clone, c2);
                Assert.That(StateHasher.Compute(clone), Is.EqualTo(StateHasher.Compute(state)), $"step {i}");
            }
        }

        [Test]
        public void AiView_HidesOpponentHand_UnlessRevealed()
        {
            var engine = new GameEngine();
            var state = NewGame(5, engine);
            int viewer = state.ActivePlayerIndex;
            var realHand = state.GetOpponent(viewer).Hand.Entities.Select(e => e.Id).ToList();
            var rng = new DeterministicRng(1);

            bool anyDifferent = false;
            for (int i = 0; i < 10; i++)
            {
                var view = AiView.Create(state, viewer, false, rng);
                var hand = view.GetOpponent(viewer).Hand.Entities.Select(e => e.Id).ToList();
                Assert.That(hand.Count, Is.EqualTo(realHand.Count));
                if (!hand.OrderBy(x => x).SequenceEqual(realHand.OrderBy(x => x)))
                    anyDifferent = true;
                Assert.That(view.Rng.SaveState(), Is.Not.EqualTo(state.Rng.SaveState()), "난수 상태를 새로 정함");
            }
            Assert.That(anyDifferent, Is.True);

            var revealed = AiView.Create(state, viewer, true, rng);
            Assert.That(revealed.GetOpponent(viewer).Hand.Entities.Select(e => e.Id), Is.EqualTo(realHand));
            Assert.That(state.GetOpponent(viewer).Hand.Entities.Select(e => e.Id), Is.EqualTo(realHand), "원본은 그대로");
        }

        [Test]
        public void LegalCommands_DecisionAnswers_CoverOrdersAndOptionalChoices()
        {
            var g = new TestGame { AutoDecide = false };
            var optional = TestCards.Effect(TriggerType.OnEnter, TargetType.EnemyHero, ActionType.Damage, 1);
            optional.Activation = EffectActivation.Optional;
            g.Play(g.AddToHand(0, TestCards.Spell("c_mix", 0,
                TestCards.Effect(TriggerType.OnEnter, TargetType.EnemyHero, ActionType.Damage, 1), optional)));

            var answers = LegalCommands.Generate(g.Engine, g.State, 0);
            // 강제 효과가 남아 있으므로 패스는 없고, 두 효과 중 하나를 올리는 답 2개
            Assert.That(answers.Count, Is.EqualTo(2));
            Assert.That(answers.All(a => a is ChainActivateCommand), Is.True);
            Assert.That(answers.All(a => g.Validate(a).IsValid), Is.True);

            // 강제 효과를 올리고 나면 선택 효과만 남는다 → 올리기 또는 패스
            var mandatory = g.State.PendingTriggers.First(t => !t.IsOptional).Id;
            g.Apply(new ChainActivateCommand(0, mandatory));
            var next = LegalCommands.Generate(g.Engine, g.State, 0);
            Assert.That(next.Count, Is.EqualTo(2));
            Assert.That(next.OfType<PassChainCommand>().Count(), Is.EqualTo(1));
        }

        [TestCase(AiDifficulty.Easy)]
        [TestCase(AiDifficulty.Normal)]
        [TestCase(AiDifficulty.Hard)]
        [TestCase(AiDifficulty.Challenge)]
        public void EachDifficulty_PlaysFullGamesWithValidCommands(AiDifficulty difficulty)
        {
            for (int seed = 1; seed <= 2; seed++)
                RunMatch(difficulty, AiDifficulty.Easy, seed, out _);
        }

        [Test]
        public void Normal_BeatsEasy_MostOfTheTime()
        {
            int wins = 0;
            const int games = 10;
            for (int i = 0; i < games; i++)
            {
                // 자리를 번갈아 바꿔 선공 유불리를 상쇄
                bool normalFirst = i % 2 == 0;
                var winner = normalFirst
                    ? RunMatch(AiDifficulty.Normal, AiDifficulty.Easy, 100 + i, out _)
                    : RunMatch(AiDifficulty.Easy, AiDifficulty.Normal, 100 + i, out _);
                if (winner == (normalFirst ? 0 : 1))
                    wins++;
            }
            Assert.That(wins, Is.GreaterThanOrEqualTo(7), $"Normal won {wins}/{games}");
        }

        [Test]
        public void Hard_BeatsEasy_MostOfTheTime()
        {
            int wins = 0;
            const int games = 6;
            for (int i = 0; i < games; i++)
            {
                bool hardFirst = i % 2 == 0;
                var winner = hardFirst
                    ? RunMatch(AiDifficulty.Hard, AiDifficulty.Easy, 200 + i, out _)
                    : RunMatch(AiDifficulty.Easy, AiDifficulty.Hard, 200 + i, out _);
                if (winner == (hardFirst ? 0 : 1))
                    wins++;
            }
            Assert.That(wins, Is.GreaterThanOrEqualTo(5), $"Hard won {wins}/{games}");
        }
    }
}
