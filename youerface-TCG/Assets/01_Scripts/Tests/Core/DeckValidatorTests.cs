using System.Collections.Generic;
using NUnit.Framework;
using TCG.Core.Data;
using TCG.Core.Scoring;

namespace TCG.Tests.Core
{
    public class DeckValidatorTests
    {
        static RuleConfig SmallRules() => new RuleConfig { DeckSize = 4, MaxCopiesPerCard = 2 };

        static CardCatalog Catalog(params CardDefinition[] cards)
        {
            var catalog = new CardCatalog();
            foreach (var card in cards)
                catalog.Add(card);
            return catalog;
        }

        static DeckDefinition Deck(params string[] ids)
        {
            var deck = new DeckDefinition { Id = "d", Name = "덱" };
            deck.Cards.AddRange(ids);
            return deck;
        }

        [Test]
        public void Check_ValidDeck_HasNoIssuesAndSummaries()
        {
            var a = TestCards.Minion("a", cost: 1, attack: 1, health: 2);
            var b = TestCards.Minion("b", cost: 9, attack: 9, health: 10);
            var catalog = Catalog(a, b);
            var report = DeckValidator.Check(Deck("a", "a", "b", "b"), catalog, SmallRules());

            Assert.That(report.IsValid, Is.True);
            Assert.That(report.CardCount, Is.EqualTo(4));
            Assert.That(report.CostCurve[1], Is.EqualTo(2));
            Assert.That(report.CostCurve[DeckReport.CostCurveBuckets - 1], Is.EqualTo(2), "코스트 7 이상은 마지막 칸");
            Assert.That(report.Count(CardType.Minion), Is.EqualTo(4));

            var table = ScoreTable.CreateDefault();
            int sa = ScoreCalculator.Calculate(a, table);
            int sb = ScoreCalculator.Calculate(b, table);
            Assert.That(report.TotalScore, Is.EqualTo(2 * sa + 2 * sb));
            Assert.That(report.MaxScore, Is.EqualTo(System.Math.Max(sa, sb)));
        }

        [Test]
        public void Check_ReportsSizeCopiesMissingAndInvalidCards_InOrder()
        {
            var good = TestCards.Minion("good");
            var bad = TestCards.Minion("bad", attack: 1, health: 0);
            var catalog = Catalog(good, bad);
            var report = DeckValidator.Check(Deck("good", "good", "good", "nope", "bad"), catalog, SmallRules());

            var kinds = new List<DeckIssueKind>();
            foreach (var issue in report.Issues)
                kinds.Add(issue.Kind);
            Assert.That(kinds, Is.EqualTo(new[] { DeckIssueKind.WrongSize, DeckIssueKind.TooManyCopies, DeckIssueKind.MissingCard, DeckIssueKind.InvalidCard }));
            Assert.That(report.Issues[0].CardId, Is.Null);
            Assert.That(report.HasIssue("good"), Is.True);
            Assert.That(report.HasIssue("nope"), Is.True);
            Assert.That(report.HasIssue("bad"), Is.True);
            Assert.That(report.CostCurve[3], Is.EqualTo(4), "없는 카드는 분포에서 빠짐");
        }

        [Test]
        public void Check_SummonCardCannotBeInDeck()
        {
            var ember = TestCards.Ember();
            ember.Id = "ember";
            var report = DeckValidator.Check(Deck("ember"), Catalog(ember), new RuleConfig { DeckSize = 1 });
            Assert.That(report.Issues.Count, Is.EqualTo(1));
            Assert.That(report.Issues[0].Kind, Is.EqualTo(DeckIssueKind.InvalidCard));
        }

        [Test]
        public void Check_EmptyOrNullDeck_OnlySizeIssue()
        {
            var report = DeckValidator.Check(null, new CardCatalog(), SmallRules());
            Assert.That(report.Issues.Count, Is.EqualTo(1));
            Assert.That(report.MaxScore, Is.EqualTo(0));
            Assert.That(DeckValidator.Validate(null, new CardCatalog(), SmallRules()), Is.Not.Empty);
        }

        [Test]
        public void CanAdd_RespectsSizeCopiesAndCardRules()
        {
            var rules = SmallRules();
            var a = TestCards.Minion("a");
            var b = TestCards.Minion("b");
            var ember = TestCards.Ember();
            ember.Id = "ember";

            Assert.That(DeckValidator.CanAdd(Deck("a"), a, rules, out _), Is.True);
            Assert.That(DeckValidator.CanAdd(Deck("a", "a"), a, rules, out var copies), Is.False);
            Assert.That(copies, Does.Contain("2장"));
            Assert.That(DeckValidator.CanAdd(Deck("a", "a", "b", "c"), b, rules, out var full), Is.False);
            Assert.That(full, Does.Contain("가득"));
            Assert.That(DeckValidator.CanAdd(Deck(), ember, rules, out _), Is.False);
        }

        [Test]
        public void Editing_AddKeepsCopiesTogether_RemoveTakesLast()
        {
            var deck = Deck("a", "b");
            DeckEditing.Add(deck, "a");
            DeckEditing.Add(deck, "c");
            Assert.That(deck.Cards, Is.EqualTo(new[] { "a", "a", "b", "c" }));
            Assert.That(DeckEditing.Count(deck, "a"), Is.EqualTo(2));

            Assert.That(DeckEditing.RemoveOne(deck, "a"), Is.True);
            Assert.That(DeckEditing.RemoveOne(deck, "x"), Is.False);
            Assert.That(deck.Cards, Is.EqualTo(new[] { "a", "b", "c" }));

            var distinct = DeckEditing.Distinct(Deck("b", "a", "b"));
            Assert.That(distinct.Count, Is.EqualTo(2));
            Assert.That(distinct[0].Id, Is.EqualTo("b"));
            Assert.That(distinct[0].Copies, Is.EqualTo(2));
        }

        [Test]
        public void Editing_SortByCostThenNameThenId_MissingLast()
        {
            var cheap = TestCards.Minion("z_cheap", cost: 1);
            var x = TestCards.Minion("x", cost: 3);
            x.Name = "나";
            var y = TestCards.Minion("y", cost: 3);
            y.Name = "가";
            var deck = Deck("missing", "x", "z_cheap", "y", "x");
            DeckEditing.Sort(deck, Catalog(cheap, x, y));
            Assert.That(deck.Cards, Is.EqualTo(new[] { "z_cheap", "y", "x", "x", "missing" }));
        }

        [Test]
        public void Editing_RemoveMissing_DropsUnknownCardsOnly()
        {
            var deck = Deck("a", "gone", "a", "gone", "b");
            int removed = DeckEditing.RemoveMissing(deck, Catalog(TestCards.Minion("a"), TestCards.Minion("b")));
            Assert.That(removed, Is.EqualTo(2));
            Assert.That(deck.Cards, Is.EqualTo(new[] { "a", "a", "b" }));
            Assert.That(DeckEditing.Uses(deck, "a"), Is.True);
            Assert.That(DeckEditing.Uses(deck, "gone"), Is.False);
        }
    }
}
