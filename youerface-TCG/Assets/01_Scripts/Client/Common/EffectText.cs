using System.Text;
using TCG.Core.Data;

namespace TCG.Client.Common
{
    /// <summary>효과 정의를 한국어 설명으로 (예: "등장 시: 무작위 적 기물 1에게 2 피해").</summary>
    public static class EffectText
    {
        public static string Describe(EffectDefinition effect)
        {
            if (effect?.Trigger == null || effect.Action == null)
                return "";
            var sb = new StringBuilder();
            sb.Append(Trigger(effect.Trigger.Type)).Append(": ");
            if (effect.Conditions != null)
            {
                foreach (var c in effect.Conditions)
                {
                    if (c != null)
                        sb.Append(Condition(c)).Append(", ");
                }
            }
            sb.Append(Action(effect));
            return sb.ToString();
        }

        public static string Trigger(TriggerType type)
        {
            switch (type)
            {
                case TriggerType.OnEnter: return "등장 시";
                case TriggerType.OnExit: return "퇴장 시";
                case TriggerType.OnTurnStart: return "내 턴 시작 시";
                case TriggerType.OnTurnEnd: return "내 턴 종료 시";
                case TriggerType.OnEffectDamageTaken: return "효과 피해를 받을 때";
                case TriggerType.OnCombatDamageTaken: return "전투 피해를 받을 때";
                case TriggerType.OnAnyDamageTaken: return "모든 피해를 받을 때";
                case TriggerType.OnEffectDamageDealt: return "효과 피해를 줄 때";
                case TriggerType.OnCombatDamageDealt: return "전투 피해를 줄 때";
                case TriggerType.OnAnyDamageDealt: return "모든 피해를 줄 때";
                case TriggerType.OnAttack: return "공격 시";
                case TriggerType.OnCardPlayed: return "카드 사용 시";
                case TriggerType.Aura: return "상시";
                default: return type.ToString();
            }
        }

        static string Condition(ConditionDefinition c)
        {
            switch (c.Type)
            {
                case ConditionType.HealthAtMost: return $"체력 {c.Value} 이하일 때";
                case ConditionType.FriendlyMinionCountAtLeast: return $"내 기물이 {c.Value}개 이상일 때";
                case ConditionType.HandCountAtMost: return $"손패가 {c.Value}장 이하일 때";
                case ConditionType.CardTypeExists:
                    return c.CardType.HasValue ? $"내 {KoreanTerms.Of(c.CardType.Value)}이(가) 있을 때" : "조건";
                default: return c.Type.ToString();
            }
        }

        static string Target(TargetDefinition t)
        {
            if (t == null)
                return "";
            switch (t.Type)
            {
                case TargetType.Self: return "자신";
                case TargetType.FriendlyHero: return "내 영웅";
                case TargetType.EnemyHero: return "상대 영웅";
                case TargetType.RandomEnemyMinion: return $"무작위 적 기물 {t.Count ?? 1}";
                case TargetType.AllEnemyMinions: return "모든 적 기물";
                case TargetType.AllFriendlyMinions: return "모든 아군 기물";
                case TargetType.Chosen: return "선택한 대상";
                default: return t.Type.ToString();
            }
        }

        static string Signed(int value) => value >= 0 ? $"+{value}" : value.ToString();

        static bool TargetsEnemyPlayer(TargetDefinition t) => t != null && t.Type == TargetType.EnemyHero;

        static string Action(EffectDefinition e)
        {
            var a = e.Action;
            int n = a.Amount ?? 0;
            string t = Target(e.Target);
            switch (a.Type)
            {
                case ActionType.Damage: return $"{t}에게 {n} 피해";
                case ActionType.Heal: return $"{t} 체력 {n} 회복";
                case ActionType.Draw: return TargetsEnemyPlayer(e.Target) ? $"상대가 카드 {a.Amount ?? 1}장 드로우" : $"카드 {a.Amount ?? 1}장 드로우";
                case ActionType.Discard: return TargetsEnemyPlayer(e.Target) ? $"상대 손패 {a.Amount ?? 1}장 버리기" : $"내 손패 {a.Amount ?? 1}장 버리기";
                case ActionType.ModifyAttack: return $"{t} 공격력 {Signed(n)}";
                case ActionType.ModifyHealth: return $"{t} 체력 {Signed(n)}";
                case ActionType.Destroy: return $"{t} 파괴";
                case ActionType.CreateSummon:
                    return a.Summon == null ? "소환물 생성" : $"소환물 '{a.Summon.Name}' 생성 (사용 {a.Summon.Usage ?? 0}회)";
                case ActionType.ModifyUsage: return $"{t} 사용 횟수 {Signed(n)}";
                case ActionType.ModifyCost:
                    if (e.Target?.Type == TargetType.FriendlyHero) return $"내 손패 코스트 {Signed(n)}";
                    if (e.Target?.Type == TargetType.EnemyHero) return $"상대 손패 코스트 {Signed(n)}";
                    return $"{t} 코스트 {Signed(n)}";
                default: return a.Type.ToString();
            }
        }
    }
}
