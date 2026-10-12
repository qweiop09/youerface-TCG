using System.Collections.Generic;
using TCG.Core.Data;
using TCG.Core.Rules;
using TCG.Core.State;

namespace TCG.Core.Effects
{
    // 효과 블록 인터페이스. 새 블록 = 열거형 항목 추가 + 아래 인터페이스 구현 + EffectRegistry에 등록.
    // 엔진과 기존 블록은 수정하지 않는다.

    /// <summary>발동 중인 효과 1개의 문맥.</summary>
    public sealed class EffectContext
    {
        public RuleContext Rules { get; }
        public GameState State => Rules.State;
        /// <summary>효과를 가진 카드.</summary>
        public Entity Source { get; }
        public int OwnerIndex => Source.OwnerIndex;
        public EffectDefinition Effect { get; }
        /// <summary>카드 효과 목록(키워드 효과 포함)에서의 순번. 소환물 키 생성에 쓴다.</summary>
        public int EffectIndex { get; }
        /// <summary>카드를 낼 때 플레이어가 고른 대상.</summary>
        public int? ChosenTargetId { get; }

        public EffectContext(RuleContext rules, Entity source, EffectDefinition effect, int effectIndex, int? chosenTargetId)
        {
            Rules = rules;
            Source = source;
            Effect = effect;
            EffectIndex = effectIndex;
            ChosenTargetId = chosenTargetId;
        }

        public PlayerState Owner => State.GetPlayer(OwnerIndex);
        public PlayerState Opponent => State.GetOpponent(OwnerIndex);
    }

    public interface IConditionEvaluator
    {
        ConditionType Type { get; }
        bool Evaluate(EffectContext context, ConditionDefinition condition);
    }

    public interface ITargetSelector
    {
        TargetType Type { get; }
        /// <summary>무작위 선택인가. 무작위·선택 대상은 상시(Aura) 효과에 쓸 수 없다.</summary>
        bool IsRandom { get; }
        /// <summary>플레이어가 카드를 낼 때 대상을 골라야 하는가.</summary>
        bool RequiresChoice { get; }
        /// <summary>대상 ID(영웅 또는 카드)를 결정적 순서로 results에 추가한다.</summary>
        void Select(EffectContext context, TargetDefinition target, List<int> results);
    }

    public interface IActionHandler
    {
        ActionType Type { get; }
        /// <summary>대상 블록을 쓰는가. false면 대상 선택을 건너뛴다(예: 소환물 생성).</summary>
        bool UsesTargets { get; }
        void Execute(EffectContext context, IReadOnlyList<int> targets);
    }

    /// <summary>상시(Aura) 효과로 쓸 수 있는 행동. 매 처리 단계마다 다시 계산되는 보정값을 더한다.</summary>
    public interface IAuraActionHandler : IActionHandler
    {
        void ApplyAura(EffectContext context, IReadOnlyList<int> targets);
    }

    /// <summary>키워드 정보: 붙일 수 있는 카드 종류와, 키워드가 뜻하는 암묵적 효과.</summary>
    public sealed class KeywordInfo
    {
        public Keyword Keyword { get; }
        public IReadOnlyList<CardType> AllowedCardTypes { get; }
        /// <summary>키워드가 가진 효과(예: 선봉 = 등장 시 자신의 주인 1장 드로우). 규칙 판정형 키워드(도발 등)는 비어 있다.</summary>
        public IReadOnlyList<EffectDefinition> ImplicitEffects { get; }

        public KeywordInfo(Keyword keyword, CardType[] allowedCardTypes, params EffectDefinition[] implicitEffects)
        {
            Keyword = keyword;
            AllowedCardTypes = allowedCardTypes;
            ImplicitEffects = implicitEffects;
        }

        public bool IsAllowedOn(CardType type)
        {
            for (int i = 0; i < AllowedCardTypes.Count; i++)
            {
                if (AllowedCardTypes[i] == type)
                    return true;
            }
            return false;
        }
    }
}
