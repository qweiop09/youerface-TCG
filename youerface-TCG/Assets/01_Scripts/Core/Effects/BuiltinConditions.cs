using TCG.Core.Data;
using TCG.Core.State;

namespace TCG.Core.Effects
{
    /// <summary>체력 n 이하: 효과를 가진 카드의 체력, 체력이 없는 카드면 주인 영웅의 체력.</summary>
    public sealed class HealthAtMostCondition : IConditionEvaluator
    {
        public ConditionType Type => ConditionType.HealthAtMost;

        public bool Evaluate(EffectContext context, ConditionDefinition condition)
        {
            int health = context.Source.Health ?? context.Owner.Hero.Health;
            return health <= condition.Value;
        }
    }

    /// <summary>내 기물 n개 이상.</summary>
    public sealed class FriendlyMinionCountAtLeastCondition : IConditionEvaluator
    {
        public ConditionType Type => ConditionType.FriendlyMinionCountAtLeast;

        public bool Evaluate(EffectContext context, ConditionDefinition condition)
        {
            return context.Owner.Minions.Count >= condition.Value;
        }
    }

    /// <summary>내 손패 n장 이하.</summary>
    public sealed class HandCountAtMostCondition : IConditionEvaluator
    {
        public ConditionType Type => ConditionType.HandCountAtMost;

        public bool Evaluate(EffectContext context, ConditionDefinition condition)
        {
            return context.Owner.Hand.Count <= condition.Value;
        }
    }

    /// <summary>내 보드에 특정 종류 카드가 n개(최소 1) 이상 존재.</summary>
    public sealed class CardTypeExistsCondition : IConditionEvaluator
    {
        public ConditionType Type => ConditionType.CardTypeExists;

        public bool Evaluate(EffectContext context, ConditionDefinition condition)
        {
            if (!condition.CardType.HasValue)
                return false;

            int required = condition.Value < 1 ? 1 : condition.Value;
            int count = 0;
            var zones = context.Owner.BoardZones;
            for (int z = 0; z < zones.Count; z++)
            {
                var entities = zones[z].Entities;
                for (int i = 0; i < entities.Count; i++)
                {
                    if (entities[i].Type == condition.CardType.Value)
                        count++;
                }
            }
            return count >= required;
        }
    }
}
