using System.Collections.Generic;
using TCG.Core.Data;

namespace TCG.Tests.Core
{
    /// <summary>테스트용 카드 정의 모음.</summary>
    static class TestCards
    {
        public static CardDefinition Minion(string id = "c_minion", int cost = 3, int attack = 3, int health = 2)
        {
            return new CardDefinition
            {
                Id = id,
                Name = "불꽃 정령",
                Type = CardType.Minion,
                Cost = cost,
                Attack = attack,
                Health = health,
            };
        }

        /// <summary>Architecture §4 예시와 같은 하수인: 도발, 등장 시 무작위 적 하수인 1에게 2 피해.</summary>
        public static CardDefinition FireSpirit()
        {
            var card = Minion("c_7f3a");
            card.Keywords.Add(Keyword.Taunt);
            card.Art.Image = "sha256:ab12";
            card.Sounds.Enter = "sha256:cd34";
            card.Sounds.Exit = "sha256:ef56";
            card.Effects.Add(new EffectDefinition
            {
                Trigger = new TriggerDefinition { Type = TriggerType.OnEnter },
                Target = new TargetDefinition { Type = TargetType.RandomEnemyMinion, Count = 1 },
                Action = new ActionDefinition { Type = ActionType.Damage, Amount = 2 },
            });
            return card;
        }

        public static CardDefinition Ember(int usage = 2)
        {
            var summon = new CardDefinition
            {
                Name = "불씨",
                Type = CardType.Summon,
                Usage = usage,
            };
            summon.Art.Image = "sha256:9999";
            summon.Effects.Add(new EffectDefinition
            {
                Trigger = new TriggerDefinition { Type = TriggerType.OnTurnEnd },
                Target = new TargetDefinition { Type = TargetType.EnemyHero },
                Action = new ActionDefinition { Type = ActionType.Damage, Amount = 1 },
            });
            return summon;
        }

        /// <summary>등장 시 소환물 "불씨"를 만드는 주문.</summary>
        public static CardDefinition SummonSpell(CardDefinition summon = null)
        {
            var card = new CardDefinition
            {
                Id = "c_spell",
                Name = "불씨 피우기",
                Type = CardType.Spell,
                Cost = 2,
            };
            card.Effects.Add(new EffectDefinition
            {
                Trigger = new TriggerDefinition { Type = TriggerType.OnEnter },
                Conditions = new List<ConditionDefinition>
                {
                    new ConditionDefinition { Type = ConditionType.CardTypeExists, Value = 1, CardType = CardType.Field },
                },
                Target = new TargetDefinition { Type = TargetType.Self },
                Action = new ActionDefinition { Type = ActionType.CreateSummon, Summon = summon ?? Ember() },
            });
            return card;
        }

        public static CardDefinition Companion(int? usage)
        {
            return new CardDefinition { Id = "c_companion", Name = "동료", Type = CardType.Companion, Cost = 1, Usage = usage };
        }

        public static CardDefinition Field()
        {
            return new CardDefinition { Id = "c_field", Name = "필드", Type = CardType.Field, Cost = 2 };
        }
    }
}
