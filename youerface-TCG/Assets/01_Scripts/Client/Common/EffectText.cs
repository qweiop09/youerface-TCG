using System.Collections.Generic;
using System.Text;
using TCG.Core.Data;

namespace TCG.Client.Common
{
    /// <summary>
    /// 카드·효과 설명문 생성기 (GameDesign §11.1). 카드 제작기 미리보기·덱 편집·대전 화면이 모두 이것을 쓴다.
    /// - 효과 1개: "등장 시: 무작위 적 기물 1개에게 2 피해를 준다."
    /// - 카드 본문: 트리거·조건·발동 방식이 같은 효과를 한 문장으로 묶는다.
    ///   예) "내가 다른 카드를 낼 때: 무작위 적 기물 1개에게 1 피해를 주고, 내 영웅의 체력을 1 회복한다."
    /// 문구에는 "하수인"이 아니라 "기물"을 쓴다.
    /// </summary>
    public static class EffectText
    {
        /// <summary>효과 1개의 설명 (선택 발동 표시는 붙이지 않음).</summary>
        public static string Describe(EffectDefinition effect)
        {
            if (effect?.Trigger == null || effect.Action == null)
                return "";
            return Prefix(effect) + ": " + Clause(effect, final: true) + ".";
        }

        /// <summary>
        /// 카드 효과 전체를 묶은 문장들 (한 줄에 한 묶음). 선택 발동 묶음은 끝에 "(선택)".
        /// optionalMark: 선택 표시 문자열 (리치 텍스트 색 등). 없으면 "(선택)".
        /// </summary>
        public static List<string> Sentences(CardDefinition card, string optionalMark = null)
        {
            var result = new List<string>();
            if (card?.Effects == null)
                return result;

            // 처음 나온 순서대로 묶는다.
            var keys = new List<string>();
            var groups = new List<List<EffectDefinition>>();
            foreach (var effect in card.Effects)
            {
                if (effect?.Trigger == null || effect.Action == null)
                    continue;
                string key = GroupKey(effect);
                int index = keys.IndexOf(key);
                if (index < 0)
                {
                    keys.Add(key);
                    groups.Add(new List<EffectDefinition>());
                    index = groups.Count - 1;
                }
                groups[index].Add(effect);
            }

            foreach (var group in groups)
            {
                var sb = new StringBuilder();
                sb.Append(Prefix(group[0])).Append(": ");
                for (int i = 0; i < group.Count; i++)
                {
                    bool last = i == group.Count - 1;
                    sb.Append(Clause(group[i], final: last));
                    if (!last)
                        sb.Append(", ");
                }
                sb.Append('.');
                if (group[0].Activation == EffectActivation.Optional)
                    sb.Append(' ').Append(optionalMark ?? "(선택)");
                result.Add(sb.ToString());
            }
            return result;
        }

        /// <summary>본문 전체 (문장마다 줄바꿈).</summary>
        public static string Body(CardDefinition card, string optionalMark = null) =>
            string.Join("\n", Sentences(card, optionalMark));

        /// <summary>트리거 + 조건이 같고 발동 방식이 같으면 같은 묶음.</summary>
        static string GroupKey(EffectDefinition e)
        {
            var sb = new StringBuilder();
            sb.Append(e.Trigger.Type).Append('|').Append(e.Activation);
            if (e.Conditions != null)
            {
                foreach (var c in e.Conditions)
                {
                    if (c != null)
                        sb.Append('|').Append(c.Type).Append(':').Append(c.Value).Append(':').Append(c.CardType);
                }
            }
            return sb.ToString();
        }

        /// <summary>"트리거, 조건1, 조건2"</summary>
        static string Prefix(EffectDefinition e)
        {
            var sb = new StringBuilder(Trigger(e.Trigger.Type));
            if (e.Conditions != null)
            {
                foreach (var c in e.Conditions)
                {
                    if (c != null)
                        sb.Append(", ").Append(Condition(c));
                }
            }
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
                case TriggerType.OnCardPlayed: return "내가 다른 카드를 낼 때";
                case TriggerType.Aura: return "상시";
                default: return type.ToString();
            }
        }

        static string Condition(ConditionDefinition c)
        {
            switch (c.Type)
            {
                case ConditionType.HealthAtMost: return $"체력이 {c.Value} 이하일 때";
                case ConditionType.FriendlyMinionCountAtLeast: return $"내 기물이 {c.Value}개 이상일 때";
                case ConditionType.HandCountAtMost: return $"손패가 {c.Value}장 이하일 때";
                case ConditionType.CardTypeExists:
                    return c.CardType.HasValue ? $"내 {Josa(KoreanTerms.Of(c.CardType.Value), "이", "가")} 있을 때" : "조건";
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
                case TargetType.RandomEnemyMinion: return $"무작위 적 기물 {t.Count ?? 1}개";
                case TargetType.AllEnemyMinions: return "모든 적 기물";
                case TargetType.AllFriendlyMinions: return "모든 아군 기물";
                case TargetType.Chosen: return "선택한 대상";
                default: return t.Type.ToString();
            }
        }

        static bool TargetsEnemyPlayer(TargetDefinition t) => t != null && t.Type == TargetType.EnemyHero;

        /// <summary>행동 한 마디. final이면 "~한다", 아니면 "~하고".</summary>
        static string Clause(EffectDefinition e, bool final)
        {
            var a = e.Action;
            int n = a.Amount ?? 0;
            int abs = n < 0 ? -n : n;
            string t = Target(e.Target);
            switch (a.Type)
            {
                case ActionType.Damage:
                    return $"{t}에게 {n} 피해를 " + (final ? "준다" : "주고");
                case ActionType.Heal:
                    return $"{t}의 체력을 {n} " + (final ? "회복한다" : "회복하고");
                case ActionType.Draw:
                    return (TargetsEnemyPlayer(e.Target) ? "상대가 " : "") + $"카드를 {a.Amount ?? 1}장 " + (final ? "뽑는다" : "뽑고");
                case ActionType.Discard:
                    return (TargetsEnemyPlayer(e.Target) ? "상대 손패를 " : "내 손패를 ") + $"무작위로 {a.Amount ?? 1}장 " + (final ? "버린다" : "버리고");
                case ActionType.ModifyAttack:
                    return $"{t}의 공격력을 {abs} " + UpDown(n, final);
                case ActionType.ModifyHealth:
                    return $"{t}의 체력을 {abs} " + UpDown(n, final);
                case ActionType.Destroy:
                    return Josa(t, "을", "를") + " " + (final ? "파괴한다" : "파괴하고");
                case ActionType.CreateSummon:
                    if (a.Summon == null)
                        return final ? "소환물을 만든다" : "소환물을 만들고";
                    return $"소환물 '{a.Summon.Name}'(사용 {a.Summon.Usage ?? 0}회)" + JosaOnly(a.Summon.Name, "을", "를") + " " + (final ? "만든다" : "만들고");
                case ActionType.ModifyUsage:
                    return $"{t}의 사용 횟수를 {abs} " + MoreLess(n, final);
                case ActionType.ModifyCost:
                    string who = e.Target?.Type == TargetType.FriendlyHero ? "내 손패" : e.Target?.Type == TargetType.EnemyHero ? "상대 손패" : t;
                    return $"{who}의 코스트를 {abs} " + MoreLess(n, final);
                default:
                    return a.Type.ToString();
            }
        }

        static string UpDown(int n, bool final) => n >= 0 ? (final ? "올린다" : "올리고") : (final ? "내린다" : "내리고");
        static string MoreLess(int n, bool final) => n >= 0 ? (final ? "늘린다" : "늘리고") : (final ? "줄인다" : "줄이고");

        /// <summary>단어 + 받침에 맞는 조사 ("기물을", "자신을", "영웅을" …). 한글이 아니면 받침 없음으로 본다.</summary>
        public static string Josa(string word, string withFinal, string withoutFinal) => word + JosaOnly(word, withFinal, withoutFinal);

        static string JosaOnly(string word, string withFinal, string withoutFinal)
        {
            if (string.IsNullOrEmpty(word))
                return withoutFinal;
            char last = word[word.Length - 1];
            bool hasFinal = last >= 0xAC00 && last <= 0xD7A3 && (last - 0xAC00) % 28 != 0;
            return hasFinal ? withFinal : withoutFinal;
        }
    }
}
