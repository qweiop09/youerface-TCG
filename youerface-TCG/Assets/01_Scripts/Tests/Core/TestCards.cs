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

        /// <summary>Architecture §4 예시와 같은 기물: 도발, 등장 시 무작위 적 기물 1에게 2 피해.</summary>
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

        public static CardDefinition Ember(int usage = 2, SummonDuplicateMode onDuplicate = SummonDuplicateMode.AddNew)
        {
            var summon = new CardDefinition
            {
                Name = "불씨",
                Type = CardType.Summon,
                Usage = usage,
                OnDuplicate = onDuplicate,
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

        public static CardDefinition Spell(string id, int cost, params EffectDefinition[] effects)
        {
            var card = new CardDefinition { Id = id, Name = id, Type = CardType.Spell, Cost = cost };
            card.Effects.AddRange(effects);
            return card;
        }

        public static CardDefinition FieldWith(string id, params EffectDefinition[] effects)
        {
            var card = new CardDefinition { Id = id, Name = id, Type = CardType.Field, Cost = 1 };
            card.Effects.AddRange(effects);
            return card;
        }

        public static CardDefinition CompanionWith(string id, int? usage, params EffectDefinition[] effects)
        {
            var card = new CardDefinition { Id = id, Name = id, Type = CardType.Companion, Cost = 1, Usage = usage };
            card.Effects.AddRange(effects);
            return card;
        }

        public static CardDefinition MinionWith(string id, int cost, int attack, int health, params Keyword[] keywords)
        {
            var card = Minion(id, cost, attack, health);
            card.Keywords.AddRange(keywords);
            return card;
        }

        public static EffectDefinition Effect(TriggerType trigger, TargetType target, ActionType action, int? amount = null,
            CardDefinition summon = null, int? count = null, params ConditionDefinition[] conditions)
        {
            return new EffectDefinition
            {
                Trigger = new TriggerDefinition { Type = trigger },
                Conditions = new List<ConditionDefinition>(conditions),
                Target = new TargetDefinition { Type = target, Count = count },
                Action = new ActionDefinition { Type = action, Amount = amount, Summon = summon },
            };
        }

        /// <summary>등장 시 소환물을 만드는 0코스트 주문.</summary>
        public static CardDefinition SummonSpellFor(CardDefinition summon, string id = "c_summoner")
        {
            return Spell(id, 0, Effect(TriggerType.OnEnter, TargetType.Self, ActionType.CreateSummon, summon: summon));
        }
    }
}
