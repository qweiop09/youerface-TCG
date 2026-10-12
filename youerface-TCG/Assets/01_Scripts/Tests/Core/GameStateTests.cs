using NUnit.Framework;
using TCG.Core.Data;
using TCG.Core.State;

namespace TCG.Tests.Core
{
    public class GameStateTests
    {
        [Test]
        public void NewGame_HasTwoPlayersWithConfiguredHeroHealth()
        {
            var rules = new RuleConfig { HeroHealth = 25 };
            var state = new GameState(rules, seed: 42);

            Assert.That(state.Players.Count, Is.EqualTo(2));
            Assert.That(state.Phase, Is.EqualTo(GamePhase.NotStarted));
            for (int i = 0; i < 2; i++)
            {
                Assert.That(state.GetPlayer(i).Index, Is.EqualTo(i));
                Assert.That(state.GetPlayer(i).Hero.Health, Is.EqualTo(25));
                Assert.That(state.GetPlayer(i).Hero.MaxHealth, Is.EqualTo(25));
            }
            Assert.That(state.GetOpponent(0), Is.SameAs(state.GetPlayer(1)));
        }

        [Test]
        public void EntityIds_AreSequentialAndUnique()
        {
            var state = new GameState(RuleConfig.CreateDefault(), seed: 1);
            int heroA = state.GetPlayer(0).Hero.Id;
            int heroB = state.GetPlayer(1).Hero.Id;

            var e1 = state.CreateEntity(0, TestCards.Minion());
            var e2 = state.CreateEntity(1, TestCards.Minion());

            Assert.That(new[] { heroA, heroB, e1.Id, e2.Id }, Is.EqualTo(new[] { 1, 2, 3, 4 }));
            Assert.That(state.NextEntityId, Is.EqualTo(5));
        }

        [Test]
        public void Entity_CopiesDefinitionValues_AndOptionalStatsStayNull()
        {
            var state = new GameState(RuleConfig.CreateDefault(), seed: 1);

            var minion = state.CreateEntity(0, TestCards.FireSpirit());
            Assert.That(minion.Attack, Is.EqualTo(3));
            Assert.That(minion.Health, Is.EqualTo(2));
            Assert.That(minion.MaxHealth, Is.EqualTo(2));
            Assert.That(minion.Usage, Is.Null);
            Assert.That(minion.Keywords, Is.EqualTo(new[] { Keyword.Taunt }));

            var companion = state.CreateEntity(0, TestCards.Companion(null));
            Assert.That(companion.Attack, Is.Null);
            Assert.That(companion.Health, Is.Null);
            Assert.That(companion.Usage, Is.Null);

            var summon = state.CreateEntity(0, TestCards.Ember(usage: 2));
            Assert.That(summon.Usage, Is.EqualTo(2));
        }

        [Test]
        public void Entity_KeywordsAreIndependentFromDefinition()
        {
            var state = new GameState(RuleConfig.CreateDefault(), seed: 1);
            var definition = TestCards.FireSpirit();
            var entity = state.CreateEntity(0, definition);

            entity.Keywords.Clear();

            Assert.That(definition.Keywords, Has.Count.EqualTo(1));
        }

        [Test]
        public void FindEntity_SearchesAllZonesOfBothPlayers()
        {
            var state = new GameState(RuleConfig.CreateDefault(), seed: 1);
            var inHand = state.CreateEntity(0, TestCards.Minion());
            var onBoard = state.CreateEntity(1, TestCards.Minion());
            state.GetPlayer(0).Hand.TryAdd(inHand);
            state.GetPlayer(1).Minions.TryAdd(onBoard);

            Assert.That(state.FindEntity(inHand.Id), Is.SameAs(inHand));
            Assert.That(state.FindEntity(onBoard.Id), Is.SameAs(onBoard));
            Assert.That(state.FindEntity(999), Is.Null);
        }
    }
}
