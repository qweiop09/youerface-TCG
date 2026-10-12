using System.Collections.Generic;

namespace TCG.Core.Data
{
    /// <summary>
    /// 카드 정의가 GameDesign §5.1의 확정 규칙에 맞는지 검사한다.
    /// 카드 제작기와 덱 검증이 같은 코드를 쓴다. 빈 목록이면 유효.
    /// </summary>
    public static class CardValidator
    {
        /// <summary>덱에 넣는 카드(최상위 카드)를 검사한다.</summary>
        public static IReadOnlyList<string> Validate(CardDefinition card)
        {
            var errors = new List<string>();
            if (card == null)
            {
                errors.Add("카드 정의가 없습니다.");
                return errors;
            }

            if (card.Type == CardType.Summon)
                errors.Add("소환물은 다른 카드의 효과로만 생성되며 단독 카드가 될 수 없습니다.");

            ValidateCard(card, errors, card.Name);
            return errors;
        }

        static void ValidateCard(CardDefinition card, List<string> errors, string path)
        {
            if (card.Cost < 0)
                errors.Add($"[{path}] 코스트는 0 이상이어야 합니다.");

            if (card.Type == CardType.Minion)
            {
                if (card.Attack == null || card.Attack < 0)
                    errors.Add($"[{path}] 하수인은 0 이상의 공격력이 필요합니다.");
                if (card.Health == null || card.Health < 1)
                    errors.Add($"[{path}] 하수인은 1 이상의 체력이 필요합니다.");
            }
            else if (card.Attack != null || card.Health != null)
            {
                errors.Add($"[{path}] 공격력·체력은 하수인만 가질 수 있습니다.");
            }

            switch (card.Type)
            {
                case CardType.Summon:
                    if (card.Usage == null || card.Usage < 1)
                        errors.Add($"[{path}] 소환물은 1 이상의 사용 횟수가 필요합니다.");
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

            if (card.Effects == null)
                return;

            for (int i = 0; i < card.Effects.Count; i++)
            {
                var effect = card.Effects[i];
                string effectPath = $"{path}/효과{i + 1}";
                if (effect == null || effect.Trigger == null || effect.Target == null || effect.Action == null)
                {
                    errors.Add($"[{effectPath}] 트리거·대상·행동이 모두 필요합니다.");
                    continue;
                }

                var action = effect.Action;
                if (action.Type == ActionType.CreateSummon)
                {
                    if (action.Summon == null)
                        errors.Add($"[{effectPath}] 소환물 생성 효과에는 소환물 정의가 필요합니다.");
                    else if (action.Summon.Type != CardType.Summon)
                        errors.Add($"[{effectPath}] 소환물 생성 효과로는 소환물만 만들 수 있습니다.");
                    else
                        ValidateCard(action.Summon, errors, $"{effectPath}/{action.Summon.Name}");
                }
                else if (action.Summon != null)
                {
                    errors.Add($"[{effectPath}] 소환물 정의는 소환물 생성 효과에만 둘 수 있습니다.");
                }
            }
        }
    }
}
