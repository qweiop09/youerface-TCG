using System;
using System.Collections.Generic;
using TCG.Core.Effects;
using TCG.Core.Serialization;

namespace TCG.Core.Data
{
    /// <summary>
    /// 카드 제작기 편집 도우미: 새 카드·효과 기본값, 종류·행동을 바꿀 때 맞지 않는 값 정리.
    /// 규칙 판정은 하지 않는다(검사는 <see cref="CardValidator"/>).
    /// </summary>
    public static class CardEditing
    {
        public const string DefaultName = "새 카드";
        public const string DefaultSummonName = "새 소환물";

        /// <summary>덱에 넣을 수 있는 종류 (소환물은 효과 안에서만 만든다).</summary>
        public static readonly IReadOnlyList<CardType> DeckTypes = new[]
        {
            CardType.Minion, CardType.Spell, CardType.Field, CardType.Companion,
        };

        public static CardDefinition NewCard(string id, CardType type = CardType.Minion)
        {
            var card = new CardDefinition { Id = id, Name = DefaultName, Type = type, Cost = 1 };
            Normalize(card, null);
            return card;
        }

        public static CardDefinition NewSummon()
        {
            var summon = new CardDefinition { Name = DefaultSummonName, Type = CardType.Summon };
            Normalize(summon, null);
            return summon;
        }

        public static EffectDefinition NewEffect()
        {
            return new EffectDefinition
            {
                Trigger = new TriggerDefinition { Type = TriggerType.OnEnter },
                Target = new TargetDefinition { Type = TargetType.EnemyHero },
                Action = new ActionDefinition { Type = ActionType.Damage, Amount = 1 },
            };
        }

        public static ConditionDefinition NewCondition() =>
            new ConditionDefinition { Type = ConditionType.HandCountAtMost, Value = 3 };

        /// <summary>종류를 바꾸고 그 종류에 없는 값(공격력·체력·사용 횟수·중복 처리·붙일 수 없는 키워드)을 정리한다.</summary>
        public static void SetType(CardDefinition card, CardType type, EffectRegistry registry = null)
        {
            card.Type = type;
            Normalize(card, registry);
        }

        public static void Normalize(CardDefinition card, EffectRegistry registry)
        {
            registry = registry ?? EffectRegistry.Default;
            if (card.Type == CardType.Minion)
            {
                card.Attack = card.Attack ?? 1;
                card.Health = card.Health ?? 1;
            }
            else
            {
                card.Attack = null;
                card.Health = null;
                // 공격음·피격음은 기물만 쓴다.
                if (card.Sounds != null)
                {
                    card.Sounds.Attack = null;
                    card.Sounds.Damaged = null;
                }
            }

            switch (card.Type)
            {
                case CardType.Summon:
                    card.Usage = card.Usage ?? 2;
                    card.OnDuplicate = card.OnDuplicate ?? SummonDuplicateMode.AddNew;
                    card.Cost = 0;
                    break;
                case CardType.Companion:
                    card.OnDuplicate = null;
                    break;
                default:
                    card.Usage = null;
                    card.OnDuplicate = null;
                    break;
            }

            if (card.Keywords != null)
            {
                card.Keywords.RemoveAll(k => !registry.TryGetKeyword(k, out var info) || !info.IsAllowedOn(card.Type));
                // 같은 키워드 두 번 금지
                var seen = new List<Keyword>();
                card.Keywords.RemoveAll(k =>
                {
                    if (seen.Contains(k)) return true;
                    seen.Add(k);
                    return false;
                });
            }
        }

        /// <summary>붙일 수 있는 키워드 (레지스트리 등록 순서가 아니라 열거형 순서).</summary>
        public static List<Keyword> AllowedKeywords(CardType type, EffectRegistry registry = null)
        {
            registry = registry ?? EffectRegistry.Default;
            var result = new List<Keyword>();
            foreach (Keyword k in Enum.GetValues(typeof(Keyword)))
            {
                if (registry.TryGetKeyword(k, out var info) && info.IsAllowedOn(type))
                    result.Add(k);
            }
            return result;
        }

        public static void ToggleKeyword(CardDefinition card, Keyword keyword)
        {
            if (!card.Keywords.Remove(keyword))
                card.Keywords.Add(keyword);
        }

        /// <summary>행동을 바꾸고 수치·소환물을 그 행동에 맞게 정리한다.</summary>
        public static void SetAction(EffectDefinition effect, ActionType type)
        {
            var action = effect.Action ?? (effect.Action = new ActionDefinition());
            action.Type = type;
            switch (type)
            {
                case ActionType.CreateSummon:
                    action.Amount = null;
                    action.Summon = action.Summon ?? NewSummon();
                    effect.Target = new TargetDefinition { Type = TargetType.Self };
                    break;
                case ActionType.Destroy:
                    action.Amount = null;
                    action.Summon = null;
                    break;
                default:
                    action.Amount = action.Amount ?? 1;
                    action.Summon = null;
                    break;
            }
        }

        public static bool UsesAmount(ActionType type) => type != ActionType.Destroy && type != ActionType.CreateSummon;

        /// <summary>대상을 바꾼다. 무작위 적 기물은 개수 1부터.</summary>
        public static void SetTarget(EffectDefinition effect, TargetType type)
        {
            var target = effect.Target ?? (effect.Target = new TargetDefinition());
            target.Type = type;
            target.Count = type == TargetType.RandomEnemyMinion ? (target.Count ?? 1) : (int?)null;
        }

        public static void SetCondition(ConditionDefinition condition, ConditionType type)
        {
            condition.Type = type;
            condition.CardType = type == ConditionType.CardTypeExists ? (condition.CardType ?? CardType.Field) : (CardType?)null;
            if (condition.Value < 1 && type != ConditionType.HandCountAtMost)
                condition.Value = 1;
        }

        public static void Move<T>(List<T> list, int index, int delta)
        {
            int to = index + delta;
            if (index < 0 || index >= list.Count || to < 0 || to >= list.Count)
                return;
            var item = list[index];
            list.RemoveAt(index);
            list.Insert(to, item);
        }

        /// <summary>JSON 왕복으로 깊은 복사.</summary>
        public static CardDefinition Clone(CardDefinition card) => CardSerializer.Deserialize(CardSerializer.Serialize(card, false));
    }
}
