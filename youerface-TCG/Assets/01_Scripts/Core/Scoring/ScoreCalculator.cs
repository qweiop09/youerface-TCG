using System;
using System.Collections.Generic;
using TCG.Core.Data;

namespace TCG.Core.Scoring
{
    /// <summary>점수 내역 한 줄의 종류. 화면 문구는 Client가 정한다.</summary>
    public enum ScoreLineKind
    {
        Base,
        Attack,
        Health,
        Cost,
        Keyword,
        Effect,
        /// <summary>하드캡을 넘은 패널티를 되돌려 주는 줄(양수).</summary>
        PenaltyCap,
    }

    public readonly struct ScoreLine
    {
        public readonly ScoreLineKind Kind;
        /// <summary>Keyword: 키워드 값, Effect: 효과 순번, 그 외 0.</summary>
        public readonly int Index;
        public readonly int Value;

        public ScoreLine(ScoreLineKind kind, int index, int value)
        {
            Kind = kind;
            Index = index;
            Value = value;
        }
    }

    public sealed class ScoreResult
    {
        /// <summary>실제 점수(음수 가능). 카드 편집 화면과 방 규칙 검사에 쓴다.</summary>
        public int Total { get; internal set; }
        /// <summary>카드에 표시하는 점수(최소 <see cref="ScoreTable.DisplayMinimum"/>).</summary>
        public int Display { get; internal set; }
        public List<ScoreLine> Lines { get; } = new List<ScoreLine>();
    }

    /// <summary>
    /// 카드 점수(가치) 계산 (GameDesign §6.1). 카드 제작기 미리보기와 대전 시작 덱 검증이 같은 코드를 쓴다.
    /// 0 = 코스트에 맞는 카드. 정수 연산만 쓰고 배율은 단계마다 /100 한다.
    /// </summary>
    public static class ScoreCalculator
    {
        /// <summary>실제 점수(음수 가능).</summary>
        public static int Calculate(CardDefinition card, ScoreTable table) => CalculateDetailed(card, table).Total;

        /// <summary>카드에 표시하는 점수(최소 0).</summary>
        public static int CalculateDisplay(CardDefinition card, ScoreTable table) => CalculateDetailed(card, table).Display;

        public static ScoreResult CalculateDetailed(CardDefinition card, ScoreTable table)
        {
            if (card == null)
                throw new ArgumentNullException(nameof(card));
            if (table == null)
                throw new ArgumentNullException(nameof(table));

            var result = new ScoreResult();
            result.Total = Compute(card, table, nested: false, result.Lines);
            result.Display = Math.Max(result.Total, table.DisplayMinimum);
            return result;
        }

        static int Compute(CardDefinition card, ScoreTable table, bool nested, List<ScoreLine> lines)
        {
            int total = 0;
            int penalty = 0;

            void Add(ScoreLineKind kind, int index, int value, bool penaltyCounts)
            {
                if (value == 0)
                    return;
                lines?.Add(new ScoreLine(kind, index, value));
                total += value;
                if (penaltyCounts && value < 0)
                    penalty += value;
            }

            if (!nested)
                Add(ScoreLineKind.Base, 0, table.BaseScore, false);
            Add(ScoreLineKind.Attack, 0, (card.Attack ?? 0) * table.AttackWeight, false);
            Add(ScoreLineKind.Health, 0, (card.Health ?? 0) * table.HealthWeight, false);
            if (!nested)
                Add(ScoreLineKind.Cost, 0, -card.Cost * table.CostWeight, false);

            if (card.Keywords != null)
            {
                for (int i = 0; i < card.Keywords.Count; i++)
                {
                    var keyword = card.Keywords[i];
                    Add(ScoreLineKind.Keyword, (int)keyword, table.KeywordScore(keyword), true);
                }
            }

            if (card.Effects != null)
            {
                for (int i = 0; i < card.Effects.Count; i++)
                {
                    var effect = card.Effects[i];
                    if (effect != null)
                        Add(ScoreLineKind.Effect, i, EffectScore(card, effect, table), true);
                }
            }

            if (penalty < -table.PenaltyCap)
                Add(ScoreLineKind.PenaltyCap, 0, -table.PenaltyCap - penalty, false);

            return total;
        }

        static int EffectScore(CardDefinition owner, EffectDefinition effect, ScoreTable table)
        {
            var action = effect.Action;
            if (action == null)
                return 0;

            long value;
            if (action.Type == ActionType.CreateSummon)
            {
                // 소환물은 항상 효과 주인 쪽에 생긴다. 대상 배율 없음.
                value = action.Summon != null ? Compute(action.Summon, table, nested: true, null) : 0;
            }
            else
            {
                int amount = action.Type == ActionType.Destroy ? 1 : (action.Amount ?? 0);
                value = (long)table.ActionScore(action.Type) * amount * Polarity(action.Type);

                var targetType = effect.Target?.Type ?? TargetType.Self;
                int side = Side(targetType);
                value = side == 0 ? Math.Abs(value) : value * side;

                int targetMultiplier = targetType == TargetType.RandomEnemyMinion
                    ? table.RandomTargetPerCount * Math.Max(1, effect.Target?.Count ?? 1)
                    : table.TargetMultiplier(targetType);
                value = value * targetMultiplier / 100;
            }

            value = value * TriggerMultiplier(owner, effect.Trigger?.Type ?? TriggerType.OnEnter, table) / 100;

            int conditionCount = effect.Conditions?.Count ?? 0;
            for (int c = 0; c < conditionCount; c++)
                value = value * table.ConditionMultiplier / 100;

            if (effect.Activation == EffectActivation.Optional)
                value = value < 0 ? 0 : value * table.OptionalMultiplier / 100;

            return (int)value;
        }

        static int TriggerMultiplier(CardDefinition owner, TriggerType trigger, ScoreTable table)
        {
            if (owner.Usage.HasValue && table.RepeatingTriggers.Contains(trigger))
                return Math.Min(Math.Max(0, owner.Usage.Value) * table.UsageMultiplierPerUse, table.UsageMultiplierCap);
            return table.TriggerMultiplier(trigger);
        }

        /// <summary>행동이 대상 주인에게 좋으면 +1, 나쁘면 -1.</summary>
        static int Polarity(ActionType action)
        {
            switch (action)
            {
                case ActionType.Damage:
                case ActionType.Discard:
                case ActionType.Destroy:
                case ActionType.ModifyCost:
                    return -1;
                default:
                    return 1;
            }
        }

        /// <summary>아군 쪽 +1, 적 쪽 -1, 고를 수 있으면 0(유리하게 고른다고 본다).</summary>
        static int Side(TargetType target)
        {
            switch (target)
            {
                case TargetType.Self:
                case TargetType.FriendlyHero:
                case TargetType.AllFriendlyMinions:
                    return 1;
                case TargetType.EnemyHero:
                case TargetType.RandomEnemyMinion:
                case TargetType.AllEnemyMinions:
                    return -1;
                default:
                    return 0;
            }
        }
    }
}
