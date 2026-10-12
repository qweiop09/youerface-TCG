using System.Collections.Generic;
using TCG.Core.Data;

namespace TCG.Core.State
{
    // 유희왕식 체인 상태 (GameDesign §7.2). 명령 사이에 선택을 기다릴 수 있으므로 전부 GameState에 저장한다.
    // 카드는 ID로만 참조한다 (상태 복제·직렬화를 쉽게 하기 위해).

    /// <summary>발동 조건을 만족해 체인에 올라가기를 기다리는 효과.</summary>
    public sealed class PendingTrigger
    {
        /// <summary>선택 명령에서 가리키는 ID (대전 중 순차 발급).</summary>
        public int Id { get; set; }
        public int SourceId { get; set; }
        public int ControllerIndex { get; set; }
        /// <summary>카드 효과 목록(키워드 효과 포함)에서의 순번.</summary>
        public int EffectIndex { get; set; }
        public TriggerType Trigger { get; set; }
        public int? ChosenTargetId { get; set; }
        /// <summary>주문 발동인가 (보드 위에 없는 카드의 효과).</summary>
        public bool IsCast { get; set; }
        public bool IsOptional { get; set; }

        public PendingTrigger Clone() => (PendingTrigger)MemberwiseClone();
    }

    /// <summary>체인에 올라간 효과 1개.</summary>
    public sealed class ChainLink
    {
        /// <summary>체인 번호 (1부터).</summary>
        public int Number { get; set; }
        public int SourceId { get; set; }
        public int ControllerIndex { get; set; }
        public int EffectIndex { get; set; }
        public TriggerType Trigger { get; set; }
        public int? ChosenTargetId { get; set; }
        public bool IsCast { get; set; }
        /// <summary>무효화됨 (무효화 블록은 나중에 추가).</summary>
        public bool Negated { get; set; }

        public ChainLink Clone() => (ChainLink)MemberwiseClone();
    }

    public enum DecisionType
    {
        /// <summary>체인 기회: 효과 하나를 체인에 올리거나 패스 (GameDesign §7.2 규칙 3).</summary>
        ChainPriority,
    }

    /// <summary>엔진이 플레이어의 선택을 기다리는 중.</summary>
    public sealed class PendingDecision
    {
        public DecisionType Type { get; }
        public int PlayerIndex { get; }
        /// <summary>체인에 올릴 수 있는 대기 효과(PendingTrigger) ID들. (대응 효과는 블록이 생기면 추가)</summary>
        public IReadOnlyList<int> Options { get; }
        /// <summary>패스할 수 있는가. 올리지 않은 강제 효과가 남아 있으면 false.</summary>
        public bool CanPass { get; }

        public PendingDecision(DecisionType type, int playerIndex, IReadOnlyList<int> options, bool canPass)
        {
            Type = type;
            PlayerIndex = playerIndex;
            Options = options ?? new int[0];
            CanPass = canPass;
        }
    }

    public enum PendingActionType
    {
        /// <summary>공격 시 체인이 끝난 뒤 전투 피해. A = 공격자, B = 대상.</summary>
        CombatDamage,
        /// <summary>턴 종료 시 체인이 끝난 뒤 턴 넘기기. A = 턴을 끝낸 플레이어.</summary>
        FinishEndTurn,
    }

    /// <summary>체인이 모두 끝난 뒤 이어서 할 행동 (여러 단계 행동용).</summary>
    public sealed class PendingAction
    {
        public PendingActionType Type { get; }
        public int A { get; }
        public int B { get; }

        public PendingAction(PendingActionType type, int a, int b = 0)
        {
            Type = type;
            A = a;
            B = b;
        }
    }
}
