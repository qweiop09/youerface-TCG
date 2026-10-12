using System;
using System.Collections.Generic;
using TCG.Core.Data;

namespace TCG.Core.Effects
{
    /// <summary>
    /// 효과 블록·키워드 레지스트리. 키로 찾기만 하고 순회하지 않으므로 Dictionary 순서에 의존하지 않는다.
    /// 새 블록은 <see cref="CreateDefault"/>에 등록을 한 줄 추가하면 된다.
    /// </summary>
    public sealed class EffectRegistry
    {
        readonly Dictionary<ConditionType, IConditionEvaluator> _conditions = new Dictionary<ConditionType, IConditionEvaluator>();
        readonly Dictionary<TargetType, ITargetSelector> _targets = new Dictionary<TargetType, ITargetSelector>();
        readonly Dictionary<ActionType, IActionHandler> _actions = new Dictionary<ActionType, IActionHandler>();
        readonly Dictionary<Keyword, KeywordInfo> _keywords = new Dictionary<Keyword, KeywordInfo>();

        static EffectRegistry _default;

        /// <summary>기본 블록이 모두 등록된 공용 인스턴스.</summary>
        public static EffectRegistry Default => _default ?? (_default = CreateDefault());

        public void Register(IConditionEvaluator evaluator) => _conditions[evaluator.Type] = evaluator;
        public void Register(ITargetSelector selector) => _targets[selector.Type] = selector;
        public void Register(IActionHandler handler) => _actions[handler.Type] = handler;
        public void Register(KeywordInfo keyword) => _keywords[keyword.Keyword] = keyword;

        public bool TryGetCondition(ConditionType type, out IConditionEvaluator evaluator) => _conditions.TryGetValue(type, out evaluator);
        public bool TryGetTarget(TargetType type, out ITargetSelector selector) => _targets.TryGetValue(type, out selector);
        public bool TryGetAction(ActionType type, out IActionHandler handler) => _actions.TryGetValue(type, out handler);
        public bool TryGetKeyword(Keyword keyword, out KeywordInfo info) => _keywords.TryGetValue(keyword, out info);

        public IConditionEvaluator GetCondition(ConditionType type) =>
            _conditions.TryGetValue(type, out var v) ? v : throw new InvalidOperationException($"Condition block not registered: {type}");
        public ITargetSelector GetTarget(TargetType type) =>
            _targets.TryGetValue(type, out var v) ? v : throw new InvalidOperationException($"Target block not registered: {type}");
        public IActionHandler GetAction(ActionType type) =>
            _actions.TryGetValue(type, out var v) ? v : throw new InvalidOperationException($"Action block not registered: {type}");

        public static EffectRegistry CreateDefault()
        {
            var r = new EffectRegistry();

            r.Register(new HealthAtMostCondition());
            r.Register(new FriendlyMinionCountAtLeastCondition());
            r.Register(new HandCountAtMostCondition());
            r.Register(new CardTypeExistsCondition());

            r.Register(new SelfTarget());
            r.Register(new FriendlyHeroTarget());
            r.Register(new EnemyHeroTarget());
            r.Register(new RandomEnemyMinionTarget());
            r.Register(new AllEnemyMinionsTarget());
            r.Register(new AllFriendlyMinionsTarget());
            r.Register(new ChosenTarget());

            r.Register(new DamageAction());
            r.Register(new HealAction());
            r.Register(new DrawAction());
            r.Register(new DiscardAction());
            r.Register(new ModifyAttackAction());
            r.Register(new ModifyHealthAction());
            r.Register(new DestroyAction());
            r.Register(new CreateSummonAction());
            r.Register(new ModifyUsageAction());
            r.Register(new ModifyCostAction());

            var minionOnly = new[] { CardType.Minion };
            r.Register(new KeywordInfo(Keyword.Taunt, minionOnly));
            r.Register(new KeywordInfo(Keyword.Stealth, minionOnly));
            r.Register(new KeywordInfo(Keyword.Vanguard, minionOnly, SelfDraw(TriggerType.OnEnter, 1)));
            r.Register(new KeywordInfo(Keyword.Exhaust, new[] { CardType.Spell }));
            r.Register(new KeywordInfo(Keyword.TreasureChest, minionOnly, SelfDraw(TriggerType.OnExit, 1)));

            return r;
        }

        static EffectDefinition SelfDraw(TriggerType trigger, int amount)
        {
            return new EffectDefinition
            {
                Trigger = new TriggerDefinition { Type = trigger },
                Target = new TargetDefinition { Type = TargetType.Self },
                Action = new ActionDefinition { Type = ActionType.Draw, Amount = amount },
            };
        }
    }
}
