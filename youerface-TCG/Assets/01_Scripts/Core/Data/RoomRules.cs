using TCG.Core.Effects;
using TCG.Core.Scoring;

namespace TCG.Core.Data
{
    /// <summary>
    /// 온라인 방 규칙 (GameDesign §6, 2026-10-13 확정): 카드별 점수 상한만. 덱 총합 상한은 없다.
    /// </summary>
    public sealed class RoomRules
    {
        /// <summary>이 점수(<see cref="ScoreCalculator.Calculate"/> 실제 값) 이하 카드만. null이면 제한 없음.</summary>
        public int? MaxCardScore { get; set; }

        public string Describe() => MaxCardScore.HasValue ? $"점수 {MaxCardScore.Value} 이하 카드만" : "점수 제한 없음";

        /// <summary>덱 검사(<see cref="DeckValidator.Check"/>) + 점수 상한. 상한을 넘는 카드는 덱에 처음 나온 순.</summary>
        public DeckReport Check(DeckDefinition deck, CardCatalog catalog, RuleConfig rules,
            ScoreTable scores = null, EffectRegistry registry = null)
        {
            scores = scores ?? ScoreTable.CreateDefault();
            var report = DeckValidator.Check(deck, catalog, rules, scores, registry);
            if (!MaxCardScore.HasValue)
                return report;
            var distinct = DeckEditing.Distinct(deck);
            for (int i = 0; i < distinct.Count; i++)
            {
                if (!catalog.TryGet(distinct[i].Id, out var card))
                    continue;
                int score = ScoreCalculator.Calculate(card, scores);
                if (score > MaxCardScore.Value)
                    report.Issues.Add(new DeckIssue(DeckIssueKind.OverScoreLimit, card.Id,
                        $"'{card.Name ?? card.Id}'는 점수 {score}로 방 규칙(점수 {MaxCardScore.Value} 이하)을 넘습니다."));
            }
            return report;
        }
    }
}
