using System.Collections.Generic;
using TCG.Core.Effects;
using TCG.Core.Scoring;

namespace TCG.Core.Data
{
    public enum DeckIssueKind
    {
        /// <summary>장수가 <see cref="RuleConfig.DeckSize"/>와 다름.</summary>
        WrongSize,
        /// <summary>같은 카드가 <see cref="RuleConfig.MaxCopiesPerCard"/>보다 많음.</summary>
        TooManyCopies,
        /// <summary>카드 모음에 없는 ID.</summary>
        MissingCard,
        /// <summary><see cref="CardValidator"/> 검사 실패.</summary>
        InvalidCard,
        /// <summary>방 규칙의 카드 점수 상한을 넘음 (<see cref="RoomRules"/>).</summary>
        OverScoreLimit,
    }

    public readonly struct DeckIssue
    {
        public readonly DeckIssueKind Kind;
        /// <summary>문제 카드 ID. 장수 문제면 null.</summary>
        public readonly string CardId;
        public readonly string Message;

        public DeckIssue(DeckIssueKind kind, string cardId, string message)
        {
            Kind = kind;
            CardId = cardId;
            Message = message;
        }

        public override string ToString() => Message;
    }

    /// <summary>덱 검사 결과 + 요약 (덱 편집 화면·대전 시작 검사가 같이 쓴다).</summary>
    public sealed class DeckReport
    {
        /// <summary>코스트 분포 칸 수. 마지막 칸은 "그 이상" (화면 표시용 상수, 규칙값 아님).</summary>
        public const int CostCurveBuckets = 8;

        public List<DeckIssue> Issues { get; } = new List<DeckIssue>();
        public bool IsValid => Issues.Count == 0;
        public int CardCount { get; internal set; }
        /// <summary>코스트별 장수. [i] = 코스트 i, 마지막 칸은 <see cref="CostCurveBuckets"/>-1 이상. 없는 카드는 빠진다.</summary>
        public int[] CostCurve { get; } = new int[CostCurveBuckets];
        /// <summary>카드 종류별 장수 (<see cref="CardType"/> 값으로 색인).</summary>
        public int[] TypeCounts { get; } = new int[5];
        /// <summary>카드 점수(실제 값, <see cref="ScoreCalculator.Calculate"/>) 합계. 참고 정보이며 덱 제한이 아니다.</summary>
        public int TotalScore { get; internal set; }
        /// <summary>점수가 가장 높은 카드의 점수. 카드가 없으면 0.</summary>
        public int MaxScore { get; internal set; }

        public int Count(CardType type) => TypeCounts[(int)type];

        /// <summary>이 카드에 문제가 있는가 (매수 초과·없음·규칙 위반).</summary>
        public bool HasIssue(string cardId)
        {
            for (int i = 0; i < Issues.Count; i++)
            {
                if (Issues[i].CardId == cardId)
                    return true;
            }
            return false;
        }

        public List<string> Messages()
        {
            var result = new List<string>(Issues.Count);
            for (int i = 0; i < Issues.Count; i++)
                result.Add(Issues[i].Message);
            return result;
        }
    }

    /// <summary>덱 규칙 검사 (장수·같은 카드 매수·카드 유효성) + 요약.</summary>
    public static class DeckValidator
    {
        /// <summary>문제 문구만. 빈 목록이면 유효.</summary>
        public static IReadOnlyList<string> Validate(DeckDefinition deck, CardCatalog catalog, RuleConfig rules)
        {
            if (deck?.Cards == null)
                return new List<string> { "덱이 비어 있습니다." };
            return Check(deck, catalog, rules).Messages();
        }

        /// <summary>
        /// 검사와 요약을 한 번에. 문제는 장수 → 카드(덱에 처음 나온 순서)별 매수·없음·규칙 위반 순.
        /// scores·registry를 생략하면 기본 점수표·기본 블록 목록.
        /// </summary>
        public static DeckReport Check(DeckDefinition deck, CardCatalog catalog, RuleConfig rules,
            ScoreTable scores = null, EffectRegistry registry = null)
        {
            scores = scores ?? ScoreTable.CreateDefault();
            var report = new DeckReport();
            var cards = deck?.Cards ?? new List<string>();
            report.CardCount = cards.Count;

            if (cards.Count != rules.DeckSize)
                report.Issues.Add(new DeckIssue(DeckIssueKind.WrongSize, null,
                    $"덱은 {rules.DeckSize}장이어야 합니다 (현재 {cards.Count}장)."));

            var distinct = DeckEditing.Distinct(deck);
            bool anyScore = false;
            for (int i = 0; i < distinct.Count; i++)
            {
                string id = distinct[i].Id;
                int copies = distinct[i].Copies;
                if (copies > rules.MaxCopiesPerCard)
                    report.Issues.Add(new DeckIssue(DeckIssueKind.TooManyCopies, id,
                        $"'{NameOf(id, catalog)}'는 {rules.MaxCopiesPerCard}장까지 넣을 수 있습니다 (현재 {copies}장)."));

                if (!catalog.TryGet(id, out var card))
                {
                    report.Issues.Add(new DeckIssue(DeckIssueKind.MissingCard, id, $"없는 카드입니다: {id}"));
                    continue;
                }

                var cardErrors = CardValidator.Validate(card, registry);
                for (int e = 0; e < cardErrors.Count; e++)
                    report.Issues.Add(new DeckIssue(DeckIssueKind.InvalidCard, id, $"{card.Name ?? id}: {cardErrors[e]}"));

                int cost = card.Cost < 0 ? 0 : card.Cost;
                report.CostCurve[cost < DeckReport.CostCurveBuckets ? cost : DeckReport.CostCurveBuckets - 1] += copies;
                report.TypeCounts[(int)card.Type] += copies;

                int score = ScoreCalculator.Calculate(card, scores);
                report.TotalScore += score * copies;
                if (!anyScore || score > report.MaxScore)
                    report.MaxScore = score;
                anyScore = true;
            }
            return report;
        }

        /// <summary>덱에 이 카드를 한 장 더 넣을 수 있는가 (장수·매수·카드 규칙). 못 넣으면 사유.</summary>
        public static bool CanAdd(DeckDefinition deck, CardDefinition card, RuleConfig rules, out string reason,
            EffectRegistry registry = null)
        {
            reason = null;
            if (card == null)
            {
                reason = "카드가 없습니다.";
                return false;
            }
            int count = deck?.Cards?.Count ?? 0;
            if (count >= rules.DeckSize)
            {
                reason = $"덱이 가득 찼습니다 ({rules.DeckSize}장).";
                return false;
            }
            if (DeckEditing.Count(deck, card.Id) >= rules.MaxCopiesPerCard)
            {
                reason = $"같은 카드는 {rules.MaxCopiesPerCard}장까지 넣을 수 있습니다.";
                return false;
            }
            var errors = CardValidator.Validate(card, registry);
            if (errors.Count > 0)
            {
                reason = "규칙에 맞지 않는 카드입니다: " + errors[0];
                return false;
            }
            return true;
        }

        static string NameOf(string id, CardCatalog catalog) => catalog.TryGet(id, out var card) && !string.IsNullOrEmpty(card.Name) ? card.Name : id;
    }

    /// <summary>덱 편집 도우미 (덱 편집 화면용). 같은 카드는 붙여서 둔다.</summary>
    public static class DeckEditing
    {
        public readonly struct CardCount
        {
            public readonly string Id;
            public readonly int Copies;

            public CardCount(string id, int copies)
            {
                Id = id;
                Copies = copies;
            }
        }

        public static int Count(DeckDefinition deck, string id)
        {
            int copies = 0;
            var cards = deck?.Cards;
            if (cards == null)
                return 0;
            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i] == id)
                    copies++;
            }
            return copies;
        }

        /// <summary>카드 ID별 장수. 덱에 처음 나온 순서.</summary>
        public static List<CardCount> Distinct(DeckDefinition deck)
        {
            var ids = new List<string>();
            var copies = new List<int>();
            var cards = deck?.Cards;
            if (cards != null)
            {
                for (int i = 0; i < cards.Count; i++)
                {
                    int at = ids.IndexOf(cards[i]);
                    if (at < 0)
                    {
                        ids.Add(cards[i]);
                        copies.Add(1);
                    }
                    else
                    {
                        copies[at]++;
                    }
                }
            }
            var result = new List<CardCount>(ids.Count);
            for (int i = 0; i < ids.Count; i++)
                result.Add(new CardCount(ids[i], copies[i]));
            return result;
        }

        /// <summary>한 장 넣는다. 같은 카드가 있으면 그 뒤에, 없으면 끝에. 규칙 검사는 하지 않는다(<see cref="DeckValidator.CanAdd"/>).</summary>
        public static void Add(DeckDefinition deck, string id)
        {
            int last = deck.Cards.LastIndexOf(id);
            if (last < 0)
                deck.Cards.Add(id);
            else
                deck.Cards.Insert(last + 1, id);
        }

        /// <summary>한 장 뺀다 (마지막 것). 없으면 false.</summary>
        public static bool RemoveOne(DeckDefinition deck, string id)
        {
            int last = deck.Cards.LastIndexOf(id);
            if (last < 0)
                return false;
            deck.Cards.RemoveAt(last);
            return true;
        }

        /// <summary>카드 모음에 없는 카드(지운 카드 등)를 전부 뺀다. 뺀 장수를 돌려준다.</summary>
        public static int RemoveMissing(DeckDefinition deck, CardCatalog catalog)
        {
            int before = deck.Cards.Count;
            deck.Cards.RemoveAll(id => !catalog.TryGet(id, out _));
            return before - deck.Cards.Count;
        }

        /// <summary>이 카드를 쓰는 덱인가.</summary>
        public static bool Uses(DeckDefinition deck, string id) => deck?.Cards != null && deck.Cards.Contains(id);

        /// <summary>코스트 → 이름 → ID 순으로 정렬 (같은 카드끼리 붙는다). 없는 카드는 맨 뒤(ID 순).</summary>
        public static void Sort(DeckDefinition deck, CardCatalog catalog)
        {
            var keyed = new List<string>(deck.Cards);
            // List.Sort는 안정 정렬이 아니므로 비교 키로 순서를 완전히 정한다.
            keyed.Sort((a, b) =>
            {
                bool hasA = catalog.TryGet(a, out var ca);
                bool hasB = catalog.TryGet(b, out var cb);
                if (hasA != hasB)
                    return hasA ? -1 : 1;
                if (hasA)
                {
                    if (ca.Cost != cb.Cost)
                        return ca.Cost.CompareTo(cb.Cost);
                    int byName = string.CompareOrdinal(ca.Name ?? "", cb.Name ?? "");
                    if (byName != 0)
                        return byName;
                }
                return string.CompareOrdinal(a, b);
            });
            deck.Cards.Clear();
            deck.Cards.AddRange(keyed);
        }
    }
}
