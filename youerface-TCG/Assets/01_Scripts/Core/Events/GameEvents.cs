using System.Collections.Generic;
using TCG.Core.Data;
using TCG.Core.State;

namespace TCG.Core.Events
{
    // GameEngine이 상태를 바꾸며 내보내는 "일어난 일" 목록. Client는 이것만 보고 연출한다.
    // 연출 큐 구조(GameDesign Q9)는 논의 전이며, 이벤트는 그 구조와 무관하게 사실만 담는다.
    // 숨겨진 정보 가리기(상대 손패·덱)는 M6에서 플레이어 시야 필터로 처리한다.

    public abstract class GameEvent { }

    public enum ExitReason
    {
        /// <summary>체력 0 이하로 사망</summary>
        Died,
        /// <summary>효과로 파괴</summary>
        Destroyed,
        /// <summary>새 필드로 교체됨</summary>
        Replaced,
        /// <summary>사용 횟수 소진</summary>
        UsageDepleted,
    }

    public sealed class GameStartedEvent : GameEvent
    {
        public int FirstPlayerIndex { get; }
        public GameStartedEvent(int firstPlayerIndex) { FirstPlayerIndex = firstPlayerIndex; }
    }

    public sealed class TurnStartedEvent : GameEvent
    {
        public int PlayerIndex { get; }
        public int TurnNumber { get; }
        public TurnStartedEvent(int playerIndex, int turnNumber) { PlayerIndex = playerIndex; TurnNumber = turnNumber; }
    }

    public sealed class TurnEndedEvent : GameEvent
    {
        public int PlayerIndex { get; }
        public TurnEndedEvent(int playerIndex) { PlayerIndex = playerIndex; }
    }

    public sealed class CostChangedEvent : GameEvent
    {
        public int PlayerIndex { get; }
        public int MaxCost { get; }
        public int AvailableCost { get; }
        public CostChangedEvent(int playerIndex, int maxCost, int availableCost)
        {
            PlayerIndex = playerIndex;
            MaxCost = maxCost;
            AvailableCost = availableCost;
        }
    }

    public sealed class CardDrawnEvent : GameEvent
    {
        public int PlayerIndex { get; }
        public int EntityId { get; }
        public CardDrawnEvent(int playerIndex, int entityId) { PlayerIndex = playerIndex; EntityId = entityId; }
    }

    /// <summary>손패가 가득 차 뽑은 카드가 소멸됨.</summary>
    public sealed class CardBurnedEvent : GameEvent
    {
        public int PlayerIndex { get; }
        public int EntityId { get; }
        public CardBurnedEvent(int playerIndex, int entityId) { PlayerIndex = playerIndex; EntityId = entityId; }
    }

    /// <summary>덱이 비어 피로 피해를 받음.</summary>
    public sealed class FatigueEvent : GameEvent
    {
        public int PlayerIndex { get; }
        public int Damage { get; }
        public FatigueEvent(int playerIndex, int damage) { PlayerIndex = playerIndex; Damage = damage; }
    }

    public sealed class CardPlayedEvent : GameEvent
    {
        public int PlayerIndex { get; }
        public int EntityId { get; }
        public CardPlayedEvent(int playerIndex, int entityId) { PlayerIndex = playerIndex; EntityId = entityId; }
    }

    /// <summary>카드가 보드 영역에 등장.</summary>
    public sealed class CardEnteredEvent : GameEvent
    {
        public int EntityId { get; }
        public ZoneType Zone { get; }
        /// <summary>영역 목록에서의 순번.</summary>
        public int Index { get; }
        /// <summary>칸 번호 영역(기물)의 칸 번호. 그 밖에는 -1.</summary>
        public int Slot { get; }
        public CardEnteredEvent(int entityId, ZoneType zone, int index, int slot = -1)
        {
            EntityId = entityId; Zone = zone; Index = index; Slot = slot;
        }
    }

    /// <summary>카드가 보드 영역에서 퇴장.</summary>
    public sealed class CardExitedEvent : GameEvent
    {
        public int EntityId { get; }
        public ZoneType From { get; }
        public ExitReason Reason { get; }
        public CardExitedEvent(int entityId, ZoneType from, ExitReason reason) { EntityId = entityId; From = from; Reason = reason; }
    }

    /// <summary>주문을 사용함. 주문 카드는 바로 묘지(일회성이면 소멸)로 가고, 효과는 체인으로 처리된다.</summary>
    public sealed class SpellCastEvent : GameEvent
    {
        public int EntityId { get; }
        public ZoneType Destination { get; }
        public SpellCastEvent(int entityId, ZoneType destination) { EntityId = entityId; Destination = destination; }
    }

    public sealed class CardDiscardedEvent : GameEvent
    {
        public int PlayerIndex { get; }
        public int EntityId { get; }
        public CardDiscardedEvent(int playerIndex, int entityId) { PlayerIndex = playerIndex; EntityId = entityId; }
    }

    /// <summary>영역이 가득 차 효과로 놓으려던 카드가 무시됨 (GameDesign §4.3).</summary>
    public sealed class PlacementFailedEvent : GameEvent
    {
        public int PlayerIndex { get; }
        public ZoneType Zone { get; }
        public PlacementFailedEvent(int playerIndex, ZoneType zone) { PlayerIndex = playerIndex; Zone = zone; }
    }

    /// <summary>효과가 체인에 올라감 → 발동 연출.</summary>
    public sealed class EffectActivatedEvent : GameEvent
    {
        public int ChainNumber { get; }
        public int SourceId { get; }
        public int EffectIndex { get; }
        public int ControllerIndex { get; }
        public TriggerType Trigger { get; }
        public ActionType Action { get; }
        public EffectActivatedEvent(int chainNumber, int sourceId, int effectIndex, int controllerIndex, TriggerType trigger, ActionType action)
        {
            ChainNumber = chainNumber;
            SourceId = sourceId;
            EffectIndex = effectIndex;
            ControllerIndex = controllerIndex;
            Trigger = trigger;
            Action = action;
        }
    }

    /// <summary>체인의 효과 1개를 처리하기 시작함 → 처리 연출. 뒤따르는 이벤트들이 이 효과의 결과다.</summary>
    public sealed class EffectResolvingEvent : GameEvent
    {
        public int ChainNumber { get; }
        public int SourceId { get; }
        public int EffectIndex { get; }
        public ActionType Action { get; }
        public EffectResolvingEvent(int chainNumber, int sourceId, int effectIndex, ActionType action)
        {
            ChainNumber = chainNumber;
            SourceId = sourceId;
            EffectIndex = effectIndex;
            Action = action;
        }
    }

    /// <summary>무효화된 효과의 처리 차례 → 발동 실패 연출.</summary>
    public sealed class EffectNegatedEvent : GameEvent
    {
        public int ChainNumber { get; }
        public int SourceId { get; }
        public EffectNegatedEvent(int chainNumber, int sourceId) { ChainNumber = chainNumber; SourceId = sourceId; }
    }

    /// <summary>체인이 모두 처리됨.</summary>
    public sealed class ChainEndedEvent : GameEvent { }

    /// <summary>플레이어의 선택을 기다림 (체인 순서·발동 여부, 대응).</summary>
    public sealed class DecisionRequestedEvent : GameEvent
    {
        public int PlayerIndex { get; }
        public DecisionType Type { get; }
        public IReadOnlyList<int> Options { get; }
        public DecisionRequestedEvent(int playerIndex, DecisionType type, IReadOnlyList<int> options)
        {
            PlayerIndex = playerIndex;
            Type = type;
            Options = options;
        }
    }

    public sealed class AttackEvent : GameEvent
    {
        public int AttackerId { get; }
        public int TargetId { get; }
        public AttackEvent(int attackerId, int targetId) { AttackerId = attackerId; TargetId = targetId; }
    }

    public sealed class DamageDealtEvent : GameEvent
    {
        public int SourceId { get; }
        public int TargetId { get; }
        public int Amount { get; }
        /// <summary>피해 유형 (GameDesign §7.6). 피로 피해는 SourceId = 0.</summary>
        public DamageType DamageType { get; }
        public DamageDealtEvent(int sourceId, int targetId, int amount, DamageType damageType)
        {
            SourceId = sourceId; TargetId = targetId; Amount = amount; DamageType = damageType;
        }
    }

    public sealed class HealedEvent : GameEvent
    {
        public int TargetId { get; }
        public int Amount { get; }
        public HealedEvent(int targetId, int amount) { TargetId = targetId; Amount = amount; }
    }

    /// <summary>카드의 현재 공격력·체력·코스트가 바뀜 (영구 변경 또는 상시 효과).</summary>
    public sealed class StatsChangedEvent : GameEvent
    {
        public int EntityId { get; }
        public int? Attack { get; }
        public int? Health { get; }
        public int? MaxHealth { get; }
        public int Cost { get; }
        public StatsChangedEvent(int entityId, int? attack, int? health, int? maxHealth, int cost)
        {
            EntityId = entityId;
            Attack = attack;
            Health = health;
            MaxHealth = maxHealth;
            Cost = cost;
        }
    }

    public sealed class UsageChangedEvent : GameEvent
    {
        public int EntityId { get; }
        public int Usage { get; }
        public UsageChangedEvent(int entityId, int usage) { EntityId = entityId; Usage = usage; }
    }

    /// <summary>효과 연쇄가 한도를 넘어 남은 체인·대기 효과를 버림.</summary>
    public sealed class ResolutionLimitReachedEvent : GameEvent { }

    public sealed class GameEndedEvent : GameEvent
    {
        /// <summary>승자. 무승부면 null.</summary>
        public int? WinnerIndex { get; }
        public GameEndedEvent(int? winnerIndex) { WinnerIndex = winnerIndex; }
    }
}
