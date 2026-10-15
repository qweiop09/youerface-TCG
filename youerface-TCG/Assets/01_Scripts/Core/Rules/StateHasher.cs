using System.Collections.Generic;
using TCG.Core.State;

namespace TCG.Core.Rules
{
    /// <summary>
    /// 게임 상태의 결정적 해시 (FNV-1a 64비트). 양쪽 클라이언트·리플레이의 상태 일치 확인용.
    /// 순회 순서는 플레이어 → 영역 선언 순서 → 영역 안 순서로 고정.
    /// </summary>
    public static class StateHasher
    {
        const ulong OffsetBasis = 14695981039346656037UL;
        const ulong Prime = 1099511628211UL;

        public static ulong Compute(GameState state)
        {
            ulong h = OffsetBasis;
            Add(ref h, (int)state.Phase);
            Add(ref h, state.ActivePlayerIndex);
            Add(ref h, state.TurnNumber);
            Add(ref h, state.WinnerIndex ?? -1);
            Add(ref h, state.NextEntityId);

            var rng = state.Rng.SaveState();
            Add(ref h, unchecked((int)rng.S0));
            Add(ref h, unchecked((int)rng.S1));
            Add(ref h, unchecked((int)rng.S2));
            Add(ref h, unchecked((int)rng.S3));

            AddChain(ref h, state);

            for (int p = 0; p < GameState.PlayerCount; p++)
            {
                var player = state.GetPlayer(p);
                Add(ref h, player.Hero.Id);
                Add(ref h, player.Hero.Health);
                Add(ref h, player.Hero.MaxHealth);
                Add(ref h, player.MaxCost);
                Add(ref h, player.AvailableCost);
                Add(ref h, player.FatigueCount);
                Add(ref h, player.TurnsTaken);

                for (int z = 0; z < player.Zones.Count; z++)
                {
                    var zone = player.Zones[z];
                    Add(ref h, (int)zone.Type);
                    Add(ref h, zone.Count);
                    for (int i = 0; i < zone.Count; i++)
                        AddEntity(ref h, zone.Entities[i]);
                }
            }
            return h;
        }

        static void AddChain(ref ulong h, GameState state)
        {
            Add(ref h, state.ChainClosed ? 1 : 0);
            Add(ref h, state.PriorityPlayer);
            Add(ref h, state.PriorityPasses);
            Add(ref h, state.NextTriggerId);
            Add(ref h, state.ResolutionCount);

            Add(ref h, state.Chain.Count);
            for (int i = 0; i < state.Chain.Count; i++)
            {
                var link = state.Chain[i];
                Add(ref h, link.Number);
                Add(ref h, link.SourceId);
                Add(ref h, link.ControllerIndex);
                Add(ref h, link.EffectIndex);
                Add(ref h, (int)link.Trigger);
                Add(ref h, link.ChosenTargetId ?? -1);
                Add(ref h, link.IsCast ? 1 : 0);
                Add(ref h, link.Negated ? 1 : 0);
            }

            Add(ref h, state.PendingTriggers.Count);
            for (int i = 0; i < state.PendingTriggers.Count; i++)
            {
                var t = state.PendingTriggers[i];
                Add(ref h, t.Id);
                Add(ref h, t.SourceId);
                Add(ref h, t.ControllerIndex);
                Add(ref h, t.EffectIndex);
                Add(ref h, (int)t.Trigger);
                Add(ref h, t.ChosenTargetId ?? -1);
                Add(ref h, t.IsCast ? 1 : 0);
                Add(ref h, t.IsOptional ? 1 : 0);
            }

            var decision = state.Decision;
            Add(ref h, decision == null ? -1 : (int)decision.Type);
            if (decision != null)
            {
                Add(ref h, decision.PlayerIndex);
                Add(ref h, decision.CanPass ? 1 : 0);
                Add(ref h, decision.Options.Count);
                for (int i = 0; i < decision.Options.Count; i++)
                    Add(ref h, decision.Options[i]);
            }

            Add(ref h, state.PendingActions.Count);
            for (int i = 0; i < state.PendingActions.Count; i++)
            {
                Add(ref h, (int)state.PendingActions[i].Type);
                Add(ref h, state.PendingActions[i].A);
                Add(ref h, state.PendingActions[i].B);
            }
        }

        static void AddEntity(ref ulong h, Entity e)
        {
            Add(ref h, e.Id);
            Add(ref h, e.OwnerIndex);
            Add(ref h, e.Definition.Id);
            Add(ref h, (int)e.Type);
            Add(ref h, e.BaseCost);
            Add(ref h, e.AuraCost);
            Add(ref h, e.BaseAttack ?? int.MinValue);
            Add(ref h, e.AuraAttack);
            Add(ref h, e.BaseMaxHealth ?? int.MinValue);
            Add(ref h, e.AuraHealth);
            Add(ref h, e.Damage);
            Add(ref h, e.Usage ?? int.MinValue);
            Add(ref h, e.PlayedThisTurn ? 1 : 0);
            Add(ref h, e.AttacksThisTurn);
            Add(ref h, e.SummonKey);
            Add(ref h, e.Slot);
            Add(ref h, e.Keywords.Count);
            for (int i = 0; i < e.Keywords.Count; i++)
                Add(ref h, (int)e.Keywords[i]);
        }

        static void Add(ref ulong h, int value)
        {
            unchecked
            {
                uint v = (uint)value;
                for (int i = 0; i < 4; i++)
                {
                    h ^= (byte)(v >> (i * 8));
                    h *= Prime;
                }
            }
        }

        static void Add(ref ulong h, string value)
        {
            if (value == null)
            {
                Add(ref h, -1);
                return;
            }
            Add(ref h, value.Length);
            for (int i = 0; i < value.Length; i++)
                Add(ref h, value[i]);
        }
    }
}
