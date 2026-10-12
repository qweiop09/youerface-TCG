using System.Collections.Generic;

namespace TCG.Core.Data
{
    /// <summary>효과 1개 = 트리거 + 조건(0개 이상) + 대상 + 행동 (GameDesign §7).</summary>
    public sealed class EffectDefinition
    {
        public TriggerDefinition Trigger { get; set; } = new TriggerDefinition();
        /// <summary>강제/선택 발동. 기본은 강제.</summary>
        public EffectActivation Activation { get; set; } = EffectActivation.Mandatory;
        public List<ConditionDefinition> Conditions { get; set; } = new List<ConditionDefinition>();
        public TargetDefinition Target { get; set; } = new TargetDefinition();
        public ActionDefinition Action { get; set; } = new ActionDefinition();
        /// <summary>발동·처리 연출 설정 (GameDesign §7.3).</summary>
        public EffectPresentation Presentation { get; set; } = new EffectPresentation();
    }

    public sealed class TriggerDefinition
    {
        public TriggerType Type { get; set; }
    }

    public sealed class ConditionDefinition
    {
        public ConditionType Type { get; set; }
        /// <summary>비교 기준값 (n 이하, n개 이상 등).</summary>
        public int Value { get; set; }
        /// <summary><see cref="ConditionType.CardTypeExists"/>에서만 사용.</summary>
        public CardType? CardType { get; set; }
    }

    public sealed class TargetDefinition
    {
        public TargetType Type { get; set; }
        /// <summary>대상 수 (무작위 n개 등). 해당 없으면 null.</summary>
        public int? Count { get; set; }
    }

    public sealed class ActionDefinition
    {
        public ActionType Type { get; set; }
        /// <summary>피해량·회복량·증감량 등. 해당 없으면 null.</summary>
        public int? Amount { get; set; }
        /// <summary><see cref="ActionType.CreateSummon"/>에서 생성할 소환물 정의.</summary>
        public CardDefinition Summon { get; set; }
    }
}
