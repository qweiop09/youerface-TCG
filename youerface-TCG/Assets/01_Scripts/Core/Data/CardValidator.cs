using System.Collections.Generic;
using TCG.Core.Effects;

namespace TCG.Core.Data
{
    /// <summary>
    /// 카드 정의가 확정 규칙(GameDesign §4.3, §5.1, §7.1)에 맞는지 검사한다.
    /// 카드 제작기와 덱 검증이 같은 코드를 쓴다. 빈 목록이면 유효.
    /// </summary>
    public static class CardValidator
    {
        /// <summary>
        /// 덱에 넣는 카드(최상위 카드)를 검사한다. registry를 생략하면 기본 블록 목록, rules를 생략하면 기본 규칙값을 쓴다
        /// (rules는 카드 설명 글자 수에만 쓴다).
        /// </summary>
        public static IReadOnlyList<string> Validate(CardDefinition card, EffectRegistry registry = null, RuleConfig rules = null)
        {
            registry = registry ?? EffectRegistry.Default;
            rules = rules ?? DefaultRules;
            var errors = new List<string>();
            if (card == null)
            {
                errors.Add("카드 정의가 없습니다.");
                return errors;
            }

            if (card.Type == CardType.Summon)
                errors.Add("소환물은 다른 카드의 효과로만 생성되며 단독 카드가 될 수 없습니다.");

            ValidateCard(card, registry, rules, errors, card.Name);
            return errors;
        }

        static readonly RuleConfig DefaultRules = RuleConfig.CreateDefault();

        static void ValidateCard(CardDefinition card, EffectRegistry registry, RuleConfig rules, List<string> errors, string path)
        {
            if (card.Flavor != null && card.Flavor.Length > rules.MaxFlavorLength)
                errors.Add($"[{path}] 카드 설명은 {rules.MaxFlavorLength}자 이하여야 합니다 (지금 {card.Flavor.Length}자).");

            if (card.Cost < 0)
                errors.Add($"[{path}] 코스트는 0 이상이어야 합니다.");

            if (card.Type == CardType.Minion)
            {
                if (card.Attack == null || card.Attack < 0)
                    errors.Add($"[{path}] 기물은 0 이상의 공격력이 필요합니다.");
                if (card.Health == null || card.Health < 1)
                    errors.Add($"[{path}] 기물은 1 이상의 체력이 필요합니다.");
            }
            else if (card.Attack != null || card.Health != null)
            {
                errors.Add($"[{path}] 공격력·체력은 기물만 가질 수 있습니다.");
            }

            switch (card.Type)
            {
                case CardType.Summon:
                    if (card.Usage == null || card.Usage < 1)
                        errors.Add($"[{path}] 소환물은 1 이상의 사용 횟수가 필요합니다.");
                    if (card.OnDuplicate == null)
                        errors.Add($"[{path}] 소환물은 중복 생성 시 처리(새로 놓기 / 사용 횟수 갱신)를 골라야 합니다.");
                    break;
                case CardType.Companion:
                    if (card.Usage != null && card.Usage < 1)
                        errors.Add($"[{path}] 동료의 사용 횟수는 비우거나(영구) 1 이상이어야 합니다.");
                    break;
                default:
                    if (card.Usage != null)
                        errors.Add($"[{path}] 사용 횟수는 소환물·동료만 가질 수 있습니다.");
                    break;
            }

            if (card.Type != CardType.Summon && card.OnDuplicate != null)
                errors.Add($"[{path}] 중복 생성 처리는 소환물만 가질 수 있습니다.");

            if (card.Keywords != null)
            {
                for (int i = 0; i < card.Keywords.Count; i++)
                {
                    var keyword = card.Keywords[i];
                    if (!registry.TryGetKeyword(keyword, out var info))
                        errors.Add($"[{path}] 등록되지 않은 키워드입니다: {keyword}");
                    else if (!info.IsAllowedOn(card.Type))
                        errors.Add($"[{path}] {keyword} 키워드는 이 카드 종류에 붙일 수 없습니다.");
                }
            }

            if (card.Effects == null)
                return;

            for (int i = 0; i < card.Effects.Count; i++)
                ValidateEffect(card.Effects[i], registry, rules, errors, $"{path}/효과{i + 1}", card.Type);
        }

        /// <summary>
        /// 이 종류의 카드에서 이 트리거가 실제로 발동할 수 있는가. 발동할 수 없는 효과는 카드에 달 수 없다 (2026-10-20 결정).
        /// - 등장 시: 모든 종류.
        /// - 주문은 보드에 남지 않으므로 그 밖의 트리거(퇴장·턴·카드 사용·상시·피해를 줄 때 등)가 발동하지 않는다.
        /// - 공격 시·전투 피해를 줄 때·피해를 받을 때: 공격·체력이 있는 기물만.
        /// </summary>
        public static bool CanTrigger(CardType type, TriggerType trigger)
        {
            if (trigger == TriggerType.OnEnter)
                return true;
            if (type == CardType.Spell)
                return false;
            switch (trigger)
            {
                case TriggerType.OnAttack:
                case TriggerType.OnCombatDamageDealt:
                case TriggerType.OnEffectDamageTaken:
                case TriggerType.OnCombatDamageTaken:
                case TriggerType.OnAnyDamageTaken:
                    return type == CardType.Minion;
                default:
                    return true;
            }
        }

        static void ValidateEffect(EffectDefinition effect, EffectRegistry registry, RuleConfig rules, List<string> errors, string path, CardType owner)
        {
            if (effect == null || effect.Trigger == null || effect.Target == null || effect.Action == null)
            {
                errors.Add($"[{path}] 트리거·대상·행동이 모두 필요합니다.");
                return;
            }

            if (!CanTrigger(owner, effect.Trigger.Type))
                errors.Add($"[{path}] 이 종류의 카드에서는 발동하지 않는 트리거입니다 (예: 주문의 \"카드 사용 시\" — 주문은 보드에 남지 않음).");

            if (effect.Conditions != null)
            {
                for (int c = 0; c < effect.Conditions.Count; c++)
                {
                    var condition = effect.Conditions[c];
                    if (condition == null || !registry.TryGetCondition(condition.Type, out _))
                        errors.Add($"[{path}] 등록되지 않은 조건입니다.");
                }
            }

            bool hasTarget = registry.TryGetTarget(effect.Target.Type, out var selector);
            if (!hasTarget)
                errors.Add($"[{path}] 등록되지 않은 대상입니다: {effect.Target.Type}");

            if (!registry.TryGetAction(effect.Action.Type, out var action))
            {
                errors.Add($"[{path}] 등록되지 않은 행동입니다: {effect.Action.Type}");
                return;
            }

            if (effect.Trigger.Type == TriggerType.Aura)
            {
                if (!(action is IAuraActionHandler))
                    errors.Add($"[{path}] {effect.Action.Type} 행동은 상시 효과로 쓸 수 없습니다.");
                if (hasTarget && (selector.IsRandom || selector.RequiresChoice))
                    errors.Add($"[{path}] 상시 효과에는 무작위·선택 대상을 쓸 수 없습니다.");
            }
            else if (hasTarget && selector.RequiresChoice && effect.Trigger.Type != TriggerType.OnEnter)
            {
                errors.Add($"[{path}] 선택 대상은 등장 시 효과에만 쓸 수 있습니다.");
            }

            if (effect.Action.Type == ActionType.CreateSummon)
            {
                var summon = effect.Action.Summon;
                if (summon == null)
                    errors.Add($"[{path}] 소환물 생성 효과에는 소환물 정의가 필요합니다.");
                else if (summon.Type != CardType.Summon)
                    errors.Add($"[{path}] 소환물 생성 효과로는 소환물만 만들 수 있습니다.");
                else
                    ValidateCard(summon, registry, rules, errors, $"{path}/{summon.Name}");
            }
            else if (effect.Action.Summon != null)
            {
                errors.Add($"[{path}] 소환물 정의는 소환물 생성 효과에만 둘 수 있습니다.");
            }
        }
    }
}
