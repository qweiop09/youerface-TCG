using System.Collections.Generic;
using TCG.Core.Data;
using TCG.Core.State;

namespace TCG.Core.Effects
{
    // 은신은 "공격 대상"만 막으므로 효과 대상 선택에서는 은신을 무시한다 (GameDesign §7.1).

    public sealed class SelfTarget : ITargetSelector
    {
        public TargetType Type => TargetType.Self;
        public bool IsRandom => false;
        public bool RequiresChoice => false;

        public void Select(EffectContext context, TargetDefinition target, List<int> results)
        {
            results.Add(context.Source.Id);
        }
    }

    public sealed class FriendlyHeroTarget : ITargetSelector
    {
        public TargetType Type => TargetType.FriendlyHero;
        public bool IsRandom => false;
        public bool RequiresChoice => false;

        public void Select(EffectContext context, TargetDefinition target, List<int> results)
        {
            results.Add(context.Owner.Hero.Id);
        }
    }

    public sealed class EnemyHeroTarget : ITargetSelector
    {
        public TargetType Type => TargetType.EnemyHero;
        public bool IsRandom => false;
        public bool RequiresChoice => false;

        public void Select(EffectContext context, TargetDefinition target, List<int> results)
        {
            results.Add(context.Opponent.Hero.Id);
        }
    }

    /// <summary>무작위 적 기물 n개 (중복 없음, 기본 1).</summary>
    public sealed class RandomEnemyMinionTarget : ITargetSelector
    {
        public TargetType Type => TargetType.RandomEnemyMinion;
        public bool IsRandom => true;
        public bool RequiresChoice => false;

        public void Select(EffectContext context, TargetDefinition target, List<int> results)
        {
            var pool = new List<Entity>();
            var minions = context.Opponent.Minions.Entities;
            for (int i = 0; i < minions.Count; i++)
            {
                if (!minions[i].IsDead)
                    pool.Add(minions[i]);
            }

            int count = target.Count ?? 1;
            for (int n = 0; n < count && pool.Count > 0; n++)
            {
                int index = context.State.Rng.Next(pool.Count);
                results.Add(pool[index].Id);
                pool.RemoveAt(index);
            }
        }
    }

    public sealed class AllEnemyMinionsTarget : ITargetSelector
    {
        public TargetType Type => TargetType.AllEnemyMinions;
        public bool IsRandom => false;
        public bool RequiresChoice => false;

        public void Select(EffectContext context, TargetDefinition target, List<int> results)
        {
            AddMinions(context.Opponent, results);
        }

        internal static void AddMinions(PlayerState player, List<int> results)
        {
            var minions = player.Minions.Entities;
            for (int i = 0; i < minions.Count; i++)
                results.Add(minions[i].Id);
        }
    }

    public sealed class AllFriendlyMinionsTarget : ITargetSelector
    {
        public TargetType Type => TargetType.AllFriendlyMinions;
        public bool IsRandom => false;
        public bool RequiresChoice => false;

        public void Select(EffectContext context, TargetDefinition target, List<int> results)
        {
            AllEnemyMinionsTarget.AddMinions(context.Owner, results);
        }
    }

    /// <summary>카드를 낼 때 플레이어가 고른 대상. 그 사이 사라졌으면 대상 없음.</summary>
    public sealed class ChosenTarget : ITargetSelector
    {
        public TargetType Type => TargetType.Chosen;
        public bool IsRandom => false;
        public bool RequiresChoice => true;

        public void Select(EffectContext context, TargetDefinition target, List<int> results)
        {
            if (context.ChosenTargetId.HasValue && context.Rules.IsValidEffectTarget(context.ChosenTargetId.Value))
                results.Add(context.ChosenTargetId.Value);
        }
    }
}
