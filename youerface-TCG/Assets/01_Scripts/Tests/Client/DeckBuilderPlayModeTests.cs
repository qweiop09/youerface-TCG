using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TCG.Client.Battle;
using TCG.Client.CardMaker;
using TCG.Client.DeckBuilder;
using TCG.Client.Flow;
using TCG.Core.AI;
using TCG.Core.Data;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace TCG.Tests.Client
{
    /// <summary>덱 편집 화면 (M5-2·M5-3)과 시작 화면 덱 선택 → 대전 (M5-4). 보관함은 임시 폴더.</summary>
    public class DeckBuilderPlayModeTests
    {
        string _dir;
        LocalCardLibrary _cards;
        LocalDeckLibrary _decks;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Application.temporaryCachePath, "DeckBuilderTests_" + Guid.NewGuid().ToString("N"));
            _cards = new LocalCardLibrary(Path.Combine(_dir, "cards"));
            _decks = new LocalDeckLibrary(Path.Combine(_dir, "decks"));
        }

        [TearDown]
        public void TearDown()
        {
            BattleLaunch.Difficulty = AiDifficulty.Normal;
            BattleLaunch.DeckKey = null;
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, true);
        }

        static CardDefinition MyMinion(string id, int cost) => new CardDefinition
        {
            Id = id, Name = "테스트 " + id, Type = CardType.Minion, Cost = cost, Attack = cost, Health = cost + 1,
        };

        /// <summary>row = 카드 줄 이름("Card 카드ID"), list = 그 줄이 들어 있는 목록 이름(CardList / DeckList).</summary>
        static Button RowButton(DeckBuilderScreen screen, string list, string cardId, string button)
        {
            foreach (var b in screen.GetComponentsInChildren<Button>(false))
            {
                var row = b.transform.parent;
                if (b.name == button && row.name == "Card " + cardId && row.parent.parent.name == list)
                    return b;
            }
            return null;
        }

        [UnityTest]
        public IEnumerator Filter_AddRemove_SaveLoadDelete()
        {
            _cards.Save(MyMinion("c_mine", 2));
            SceneManager.LoadScene(DeckBuilderScreen.SceneName);
            yield return null;
            yield return null;
            var screen = UnityEngine.Object.FindAnyObjectByType<DeckBuilderScreen>();
            screen.Cards = _cards;
            screen.Decks = _decks;
            screen.ReloadPool();

            Assert.That(screen.Pool.Catalog.TryGet("c_mine", out _), Is.True);
            Assert.That(screen.Pool.IsSample("s_squire"), Is.True);
            Assert.That(screen.Pool.IsSample("c_mine"), Is.False);
            Assert.That(screen.Deck.Cards, Is.Empty);
            Assert.That(screen.Report.IsValid, Is.False, "빈 덱은 장수 문제");

            // 필터
            screen.SetSource(DeckBuilderScreen.SourceFilter.Mine);
            Assert.That(screen.VisibleCards.Select(c => c.Id), Is.EqualTo(new[] { "c_mine" }));
            screen.SetSource(DeckBuilderScreen.SourceFilter.All);
            screen.SetTypeFilter(CardType.Spell);
            Assert.That(screen.VisibleCards, Is.Not.Empty);
            Assert.That(screen.VisibleCards.All(c => c.Type == CardType.Spell), Is.True);
            screen.SetTypeFilter(null);
            screen.SetScoreMax(0);
            Assert.That(screen.VisibleCards.All(c => screen.ScoreOf(c.Id) <= 0), Is.True);
            screen.SetScoreMax(null);
            screen.SetCostFilter(DeckReport.CostCurveBuckets - 1);
            Assert.That(screen.VisibleCards.All(c => c.Cost >= DeckReport.CostCurveBuckets - 1), Is.True);
            screen.SetCostFilter(null);
            screen.SetSort(DeckBuilderScreen.SortMode.ScoreHigh);
            for (int i = 1; i < screen.VisibleCards.Count; i++)
                Assert.That(screen.ScoreOf(screen.VisibleCards[i - 1].Id), Is.GreaterThanOrEqualTo(screen.ScoreOf(screen.VisibleCards[i].Id)));
            screen.SetSort(DeckBuilderScreen.SortMode.Cost);
            screen.SetSearch("C_MI");
            Assert.That(screen.VisibleCards.Select(c => c.Id), Is.EqualTo(new[] { "c_mine" }), "이름 검색 (대소문자 무시)");
            screen.SetSearch("2");
            Assert.That(screen.VisibleCards, Is.Not.Empty);
            Assert.That(screen.VisibleCards.All(c => c.Cost == 2), Is.True, "숫자만은 코스트");
            screen.SetSearch("기물 2코");
            Assert.That(screen.VisibleCards, Is.Not.Empty);
            Assert.That(screen.VisibleCards.All(c => c.Cost == 2 && c.Type == CardType.Minion), Is.True, "종류 + 코스트 (AND)");
            screen.SetSearch("주문");
            Assert.That(screen.VisibleCards, Is.Not.Empty);
            Assert.That(screen.VisibleCards.All(c => c.Type == CardType.Spell), Is.True);
            screen.SetSearch("피해");
            Assert.That(screen.VisibleCards, Is.Not.Empty, "효과 문구 검색");
            Assert.That(screen.VisibleCards.All(c => c.Effects.Count > 0), Is.True);
            screen.SetSearch("");
            yield return null;

            // 목록의 + 버튼으로 넣기, 같은 카드 3장째는 막힘
            RowButton(screen, "CardList", "c_mine", "Add").onClick.Invoke();
            yield return null;
            RowButton(screen, "CardList", "c_mine", "Add").onClick.Invoke();
            yield return null;
            Assert.That(DeckEditing.Count(screen.Deck, "c_mine"), Is.EqualTo(2));
            Assert.That(RowButton(screen, "CardList", "c_mine", "Add").interactable, Is.False);
            Assert.That(screen.AddCard("c_mine"), Is.False);
            Assert.That(screen.StatusText, Does.Contain("2장"));
            Assert.That(screen.Report.CostCurve[2], Is.EqualTo(2));

            // 덱 줄의 − 버튼으로 빼기
            RowButton(screen, "DeckList", "c_mine", "Remove").onClick.Invoke();
            yield return null;
            Assert.That(DeckEditing.Count(screen.Deck, "c_mine"), Is.EqualTo(1));

            // 샘플 덱 복사 → 유효 → 저장
            screen.CopySampleDeck(0);
            yield return null;
            Assert.That(screen.Report.IsValid, Is.True);
            Assert.That(screen.Deck.Id, Does.StartWith("d_"));
            Assert.That(screen.IsDirty, Is.True);
            screen.RemoveCard(screen.Deck.Cards[0]);
            screen.AddCard("c_mine");
            Assert.That(screen.Report.IsValid, Is.True);
            screen.ShowPreview("c_mine");
            screen.Save();
            string id = screen.Deck.Id;
            Assert.That(_decks.Exists(id), Is.True);
            Assert.That(screen.IsDirty, Is.False);

            string dir = Environment.GetEnvironmentVariable("TCG_CAPTURE_DIR");
            if (dir != null)
            {
                yield return null;
                yield return CardMakerPlayModeTests.CaptureScreen(Path.Combine(dir, "deckbuilder.png"));
            }

            // 복제: 새 ID, 저장 안 됨
            screen.Duplicate();
            Assert.That(screen.Deck.Id, Is.Not.EqualTo(id));
            Assert.That(screen.Deck.Name, Does.EndWith("복사본"));
            Assert.That(screen.Deck.Cards.Count, Is.EqualTo(30));
            Assert.That(screen.IsDirty, Is.True);

            // 카드를 지우면 덱에 없는 카드로 남고, 버튼으로 뺀다
            _cards.Delete("c_mine");
            screen.ReloadPool();
            Assert.That(screen.Report.Issues.Any(i => i.Kind == DeckIssueKind.MissingCard), Is.True);
            yield return null;
            Button cleanup = null;
            foreach (var b in screen.GetComponentsInChildren<Button>(false))
            {
                if (b.name.StartsWith("Button 없는 카드"))
                    cleanup = b;
            }
            Assert.That(cleanup, Is.Not.Null);
            cleanup.onClick.Invoke();
            Assert.That(screen.Deck.Cards.Count, Is.EqualTo(29));
            Assert.That(screen.Report.Issues.Any(i => i.Kind == DeckIssueKind.MissingCard), Is.False);
            _cards.Save(MyMinion("c_mine", 2));
            screen.ReloadPool();

            // 새 덱 → 내 덱에서 열기 → 삭제(두 번)
            screen.NewDeck();
            Assert.That(screen.Deck.Cards, Is.Empty);
            screen.OpenLibrary();
            Assert.That(screen.LibraryOpen, Is.True);
            screen.OpenFromLibrary(id);
            Assert.That(screen.Deck.Id, Is.EqualTo(id));
            Assert.That(DeckEditing.Count(screen.Deck, "c_mine"), Is.EqualTo(1));
            Assert.That(screen.LibraryOpen, Is.False);
            Assert.That(screen.IsDirty, Is.False);

            screen.OpenLibrary();
            screen.DeleteFromLibrary(id);
            Assert.That(_decks.Exists(id), Is.True, "첫 번째는 확인 표시만");
            screen.DeleteFromLibrary(id);
            Assert.That(_decks.Exists(id), Is.False);
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator Title_PicksMyDeck_BattleUsesIt()
        {
            // 내 카드 15종 × 2장 = 30장 덱 (유효) + 3장짜리 덱 (규칙 위반 → 고를 수는 있지만 불법 덱, 시작 불가)
            var good = new DeckDefinition { Id = "d_good", Name = "내 기물 덱" };
            for (int i = 0; i < 15; i++)
            {
                string cardId = $"c_t{i:00}";
                _cards.Save(MyMinion(cardId, 1 + i % 5));
                good.Cards.Add(cardId);
                good.Cards.Add(cardId);
            }
            _decks.Save(good);
            _decks.Save(new DeckDefinition { Id = "d_bad", Name = "모자란 덱", Cards = { "c_t00", "c_t01", "c_t02" } });

            SceneManager.LoadScene(BattleLaunch.TitleScene);
            yield return null;
            yield return null;
            var title = UnityEngine.Object.FindAnyObjectByType<TitleScreen>();
            title.ReloadDecks(_cards, _decks);

            int samples = SampleData.LoadDecks().Count;
            Assert.That(title.DeckOptions.Count, Is.EqualTo(samples + 2));
            Assert.That(title.IllegalDecks, Is.EqualTo(1));
            title.SelectDeck("d_bad");
            Assert.That(title.SelectedDeck.Illegal, Is.True);
            Assert.That(title.SelectedDeck.Label, Does.Contain("불법 덱"));
            Assert.That(title.StartButton.interactable, Is.False, "불법 덱은 대전 시작 불가");
            title.SelectDeck(title.DeckOptions[0].Key);
            Assert.That(title.StartButton.interactable, Is.True);
            Assert.That(title.SelectedDeck.IsSample, Is.True, "기본은 첫 샘플 덱");
            Assert.That(title.DeckBuilderButton, Is.Not.Null);

            title.SelectDeck("d_good");
            Assert.That(title.SelectedDeck.Deck.Name, Is.EqualTo("내 기물 덱"));
            Assert.That(BattleLaunch.DeckKey, Is.EqualTo("d_good"));
            string last = title.DeckOptions[title.DeckOptions.Count - 1].Key;
            title.SelectDeck(last);
            title.StepDeck(1);
            Assert.That(title.SelectedDeck.Key, Is.EqualTo(title.DeckOptions[0].Key), "끝에서 다음은 처음으로");
            title.StepDeck(-1);
            Assert.That(title.SelectedDeck.Key, Is.EqualTo(last));
            title.SelectDeck("d_good");

            title.StartButton.onClick.Invoke();
            yield return null;
            yield return null;

            var bootstrap = UnityEngine.Object.FindAnyObjectByType<BattleBootstrap>();
            Assert.That(bootstrap, Is.Not.Null);
            var me = bootstrap.State.GetPlayer(BattleBootstrap.Viewer);
            var mine = me.Deck.Entities.Concat(me.Hand.Entities).ToList();
            Assert.That(mine.Count, Is.GreaterThanOrEqualTo(30), "30장 + 후공 보정 카드가 있을 수 있음");
            Assert.That(mine.Where(e => e.Definition.Id.StartsWith("c_t")).Count(), Is.EqualTo(30), "내 덱 카드로 대전");
            var opponent = bootstrap.State.GetPlayer(1 - BattleBootstrap.Viewer);
            Assert.That(opponent.Deck.Entities.Concat(opponent.Hand.Entities).All(e => !e.Definition.Id.StartsWith("c_t")), Is.True, "상대는 샘플 덱");
        }
    }
}
