using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using TCG.Core.AI;
using TCG.Core.Data;
using TCG.Core.Rules;
using TCG.Core.Serialization;
using TCG.Core.State;

namespace TCG.Tests.Core
{
    /// <summary>Assets/03_Datas/Resources의 샘플 카드·덱 파일 검사.</summary>
    public class SampleDataTests
    {
        /// <summary>
        /// Unity에서는 현재 폴더가 프로젝트 루트다. 저장소 밖에서 돌릴 때는 환경 변수 TCG_SAMPLE_DIR로 지정한다.
        /// </summary>
        internal static string DataDirectory()
        {
            string fromEnv = Environment.GetEnvironmentVariable("TCG_SAMPLE_DIR");
            if (!string.IsNullOrEmpty(fromEnv) && Directory.Exists(fromEnv))
                return fromEnv;
            string unity = Path.Combine(Environment.CurrentDirectory, "Assets", "03_Datas", "Resources");
            if (Directory.Exists(unity))
                return unity;
            Assert.Ignore("Sample data directory not found.");
            return null;
        }

        internal static CardCatalog LoadCatalog()
        {
            var catalog = new CardCatalog();
            foreach (var file in Directory.GetFiles(Path.Combine(DataDirectory(), "SampleCards"), "*.json").OrderBy(f => f, StringComparer.Ordinal))
                catalog.Add(CardSerializer.Deserialize(File.ReadAllText(file)));
            return catalog;
        }

        internal static List<DeckDefinition> LoadDecks()
        {
            return Directory.GetFiles(Path.Combine(DataDirectory(), "SampleDecks"), "*.json")
                .OrderBy(f => f, StringComparer.Ordinal)
                .Select(f => DeckSerializer.Deserialize(File.ReadAllText(f)))
                .ToList();
        }

        [Test]
        public void SampleCards_LoadValidateAndRoundTrip()
        {
            var files = Directory.GetFiles(Path.Combine(DataDirectory(), "SampleCards"), "*.json");
            Assert.That(files.Length, Is.GreaterThanOrEqualTo(15));

            foreach (var file in files)
            {
                var original = JObject.Parse(File.ReadAllText(file));
                var card = CardSerializer.Deserialize(original.ToString());

                Assert.That(CardValidator.Validate(card), Is.Empty, Path.GetFileName(file));
                Assert.That(card.Id + ".json", Is.EqualTo(Path.GetFileName(file)));
                var again = JObject.Parse(CardSerializer.Serialize(card));
                Assert.That(JToken.DeepEquals(original, again), Is.True, $"{Path.GetFileName(file)} round trip");
            }
        }

        [Test]
        public void SampleDecks_AreValid()
        {
            var catalog = LoadCatalog();
            var decks = LoadDecks();
            Assert.That(decks.Count, Is.GreaterThanOrEqualTo(2));
            foreach (var deck in decks)
                Assert.That(DeckValidator.Validate(deck, catalog, RuleConfig.CreateDefault()), Is.Empty, deck.Id);
        }

        [Test]
        public void DeckValidator_CatchesSizeCopiesAndUnknownCards()
        {
            var catalog = LoadCatalog();
            var rules = RuleConfig.CreateDefault();
            var deck = new DeckDefinition { Id = "bad", Cards = { "s_squire", "s_squire", "s_squire", "nope" } };

            var errors = DeckValidator.Validate(deck, catalog, rules);

            Assert.That(errors.Count, Is.EqualTo(3)); // 장수, 매수, 없는 카드
        }

        [Test]
        public void SampleDecks_AiGamesFinish()
        {
            var catalog = LoadCatalog();
            var decks = LoadDecks();
            var engine = new GameEngine();
            for (int seed = 1; seed <= 3; seed++)
            {
                var state = new GameState(RuleConfig.CreateDefault(), seed);
                engine.StartGame(state, catalog.Resolve(decks[0]), catalog.Resolve(decks[1]));
                var ai = new[] { AiPlayers.Create(AiDifficulty.Normal, seed, engine), AiPlayers.Create(AiDifficulty.Hard, seed + 50, engine) };
                for (int i = 0; i < 3000 && state.Phase == GamePhase.InProgress; i++)
                {
                    int mover = state.Decision?.PlayerIndex ?? state.ActivePlayerIndex;
                    engine.Apply(state, ai[mover].ChooseCommand(state, mover));
                }
                Assert.That(state.Phase, Is.EqualTo(GamePhase.Finished), $"seed {seed}");
            }
        }
    }
}
