using System.Collections.Generic;
using TCG.Core.Data;
using TCG.Core.State;

namespace TCG.Core.Effects
{
    // 기본 행동 블록. 실제 상태 변경은 RuleContext의 기본 연산을 통해서만 한다.

    public sealed class DamageAction : IActionHandler
    {
        public ActionType Type => ActionType.Damage;
        public bool UsesTargets => true;

        public void Execute(EffectContext context, IReadOnlyList<int> targets)
        {
            int amount = context.Effect.Action.Amount ?? 0;
            for (int i = 0; i < targets.Count; i++)
                context.Rules.DealDamage(context.Source.Id, targets[i], amount, DamageType.Effect);
        }
    }

    public sealed class HealAction : IActionHandler
    {
        public ActionType Type => ActionType.Heal;
        public bool UsesTargets => true;

        public void Execute(EffectContext context, IReadOnlyList<int> targets)
        {
            int amount = context.Effect.Action.Amount ?? 0;
            for (int i = 0; i < targets.Count; i++)
                context.Rules.Heal(targets[i], amount);
        }
    }

    /// <summary>대상의 주인(영웅이면 그 플레이어)이 n장 드로우. 같은 플레이어는 한 번만.</summary>
    public sealed class DrawAction : IActionHandler
    {
        public ActionType Type => ActionType.Draw;
        public bool UsesTargets => true;

        public void Execute(EffectContext context, IReadOnlyList<int> targets)
        {
            int amount = context.Effect.Action.Amount ?? 1;
            var players = PlayerTargets.Resolve(context, targets);
            for (int i = 0; i < players.Count; i++)
                context.Rules.DrawCards(players[i], amount);
        }
    }

    /// <summary>대상의 주인이 손패에서 무작위 n장 버림.</summary>
    public sealed class DiscardAction : IActionHandler
    {
        public ActionType Type => ActionType.Discard;
        public bool UsesTargets => true;

        public void Execute(EffectContext context, IReadOnlyList<int> targets)
        {
            int amount = context.Effect.Action.Amount ?? 1;
            var players = PlayerTargets.Resolve(context, targets);
            for (int i = 0; i < players.Count; i++)
                context.Rules.DiscardRandom(players[i], amount);
        }
    }

    public sealed class ModifyAttackAction : IAuraActionHandler
    {
        public ActionType Type => ActionType.ModifyAttack;
        public bool UsesTargets => true;

        public void Execute(EffectContext context, IReadOnlyList<int> targets)
        {
            int amount = context.Effect.Action.Amount ?? 0;
            for (int i = 0; i < targets.Count; i++)
            {
                var e = context.Rules.FindBoardEntity(targets[i]);
                if (e == null || !e.BaseAttack.HasValue)
                    continue;
                e.BaseAttack += amount;
                e.RecordChange(StatKind.Attack, amount, context.Source, aura: false);
                context.Rules.EmitStats(e);
            }
        }

        public void ApplyAura(EffectContext context, IReadOnlyList<int> targets)
        {
            int amount = context.Effect.Action.Amount ?? 0;
            for (int i = 0; i < targets.Count; i++)
            {
                var e = context.Rules.FindBoardEntity(targets[i]);
                if (e != null && e.BaseAttack.HasValue)
                {
                    e.AuraAttack += amount;
                    e.RecordChange(StatKind.Attack, amount, context.Source, aura: true);
                }
            }
        }
    }

    /// <summary>최대 체력 증감. 현재 체력도 같은 만큼 변한다. 0 이하가 되면 사망.</summary>
    public sealed class ModifyHealthAction : IAuraActionHandler
    {
        public ActionType Type => ActionType.ModifyHealth;
        public bool UsesTargets => true;

        public void Execute(EffectContext context, IReadOnlyList<int> targets)
        {
            int amount = context.Effect.Action.Amount ?? 0;
            for (int i = 0; i < targets.Count; i++)
            {
                var e = context.Rules.FindBoardEntity(targets[i]);
                if (e == null || !e.BaseMaxHealth.HasValue)
                    continue;
                e.BaseMaxHealth += amount;
                e.RecordChange(StatKind.Health, amount, context.Source, aura: false);
                context.Rules.EmitStats(e);
                if (e.IsDead)
                    context.Rules.RemoveFromBoard(e, Events.ExitReason.Died);
            }
        }

        public void ApplyAura(EffectContext context, IReadOnlyList<int> targets)
        {
            int amount = context.Effect.Action.Amount ?? 0;
            for (int i = 0; i < targets.Count; i++)
            {
                var e = context.Rules.FindBoardEntity(targets[i]);
                if (e != null && e.BaseMaxHealth.HasValue)
                {
                    e.AuraHealth += amount;
                    e.RecordChange(StatKind.Health, amount, context.Source, aura: true);
                }
            }
        }
    }

    /// <summary>보드 위 카드를 파괴 (영웅은 대상 아님).</summary>
    public sealed class DestroyAction : IActionHandler
    {
        public ActionType Type => ActionType.Destroy;
        public bool UsesTargets => true;

        public void Execute(EffectContext context, IReadOnlyList<int> targets)
        {
            for (int i = 0; i < targets.Count; i++)
            {
                var e = context.Rules.FindBoardEntity(targets[i]);
                if (e != null)
                    context.Rules.RemoveFromBoard(e, Events.ExitReason.Destroyed);
            }
        }
    }

    /// <summary>효과 주인 쪽에 소환물을 생성. 대상 블록은 쓰지 않는다.</summary>
    public sealed class CreateSummonAction : IActionHandler
    {
        public ActionType Type => ActionType.CreateSummon;
        public bool UsesTargets => false;

        public void Execute(EffectContext context, IReadOnlyList<int> targets)
        {
            var summon = context.Effect.Action.Summon;
            if (summon == null)
                return;

            string baseKey = context.Source.SummonKey ?? context.Source.Definition.Id ?? ("entity" + context.Source.Id);
            string key = baseKey + "#" + context.EffectIndex;
            context.Rules.CreateSummon(context.OwnerIndex, summon, key);
        }
    }

    /// <summary>사용 횟수가 있는 카드(소환물·동료)의 사용 횟수 증감. 0 이하가 되면 퇴장.</summary>
    public sealed class ModifyUsageAction : IActionHandler
    {
        public ActionType Type => ActionType.ModifyUsage;
        public bool UsesTargets => true;

        public void Execute(EffectContext context, IReadOnlyList<int> targets)
        {
            int amount = context.Effect.Action.Amount ?? 0;
            for (int i = 0; i < targets.Count; i++)
            {
                var e = context.Rules.FindBoardEntity(targets[i]);
                if (e != null)
                    context.Rules.ModifyUsage(e, amount);
            }
        }
    }

    /// <summary>
    /// 코스트 증감. 대상이 영웅이면 그 플레이어의 손패 전체, 카드면 그 카드.
    /// 상시 효과로 쓰면 "내 손패 코스트 -1" 같은 지속 효과가 된다.
    /// </summary>
    public sealed class ModifyCostAction : IAuraActionHandler
    {
        public ActionType Type => ActionType.ModifyCost;
        public bool UsesTargets => true;

        public void Execute(EffectContext context, IReadOnlyList<int> targets)
        {
            int amount = context.Effect.Action.Amount ?? 0;
            var cards = Collect(context, targets);
            for (int i = 0; i < cards.Count; i++)
            {
                cards[i].BaseCost += amount;
                cards[i].RecordChange(StatKind.Cost, amount, context.Source, aura: false);
                context.Rules.EmitStats(cards[i]);
            }
        }

        public void ApplyAura(EffectContext context, IReadOnlyList<int> targets)
        {
            int amount = context.Effect.Action.Amount ?? 0;
            var cards = Collect(context, targets);
            for (int i = 0; i < cards.Count; i++)
            {
                cards[i].AuraCost += amount;
                cards[i].RecordChange(StatKind.Cost, amount, context.Source, aura: true);
            }
        }

        static List<Entity> Collect(EffectContext context, IReadOnlyList<int> targets)
        {
            var result = new List<Entity>();
            for (int i = 0; i < targets.Count; i++)
            {
                var hero = context.State.FindHero(targets[i]);
                if (hero != null)
                {
                    var hand = context.State.GetPlayer(hero.OwnerIndex).Hand.Entities;
                    for (int h = 0; h < hand.Count; h++)
                        result.Add(hand[h]);
                    continue;
                }

                var e = context.State.FindEntity(targets[i]);
                if (e != null && !result.Contains(e))
                    result.Add(e);
            }
            return result;
        }
    }

    static class PlayerTargets
    {
        /// <summary>대상 ID들을 플레이어 인덱스로 바꾼다 (등장 순서 유지, 중복 제거).</summary>
        public static List<int> Resolve(EffectContext context, IReadOnlyList<int> targets)
        {
            var players = new List<int>();
            for (int i = 0; i < targets.Count; i++)
            {
                int owner = context.Rules.OwnerOf(targets[i], context.Source);
                if (owner >= 0 && !players.Contains(owner))
                    players.Add(owner);
            }
            return players;
        }
    }
}
