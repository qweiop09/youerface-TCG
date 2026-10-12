using System;
using TCG.Core.Data;

namespace TCG.Core.Scoring
{
    /// <summary>
    /// 카드 점수(가치) 계산. 카드 제작기 미리보기와 대전 시작 덱 검증이 같은 코드를 쓴다.
    /// 확정된 것은 방향뿐이다: 공격력·체력·좋은 효과 → 증가, 코스트 → 감소, 포함된 소환물 점수는 부모에 합산.
    /// 지금 공식은 단순 선형 임시값이며, 구간 테이블·트리거 배율·패널티 하드캡 등은 사용자와 논의 후 정한다(Q7).
    /// </summary>
    public static class ScoreCalculator
    {
        public static int Calculate(CardDefinition card, ScoreTable table)
        {
            if (card == null)
                throw new ArgumentNullException(nameof(card));
            if (table == null)
                throw new ArgumentNullException(nameof(table));

            int score = CalculateRaw(card, table);
            if (table.MinScore.HasValue && score < table.MinScore.Value)
                score = table.MinScore.Value;
            return score;
        }

        static int CalculateRaw(CardDefinition card, ScoreTable table)
        {
            int score = 0;
            score += (card.Attack ?? 0) * table.AttackWeight;
            score += (card.Health ?? 0) * table.HealthWeight;
            score += (card.Usage ?? 0) * table.UsageWeight;
            score += (card.Keywords?.Count ?? 0) * table.KeywordWeight;

            if (card.Effects != null)
            {
                for (int i = 0; i < card.Effects.Count; i++)
                {
                    var effect = card.Effects[i];
                    if (effect == null)
                        continue;
                    score += table.EffectWeight;

                    var summon = effect.Action?.Summon;
                    if (effect.Action?.Type == ActionType.CreateSummon && summon != null)
                        score += CalculateRaw(summon, table);
                }
            }

            score -= card.Cost * table.CostWeight;
            return score;
        }
    }
}
