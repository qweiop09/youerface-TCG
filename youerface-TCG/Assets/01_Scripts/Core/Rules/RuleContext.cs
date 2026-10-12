using System;
using System.Collections.Generic;
using TCG.Core.Data;
using TCG.Core.Effects;
using TCG.Core.Events;
using TCG.Core.State;

namespace TCG.Core.Rules
{
    /// <summary>
    /// 명령 1개를 처리하는 동안의 작업 공간. 상태를 바꾸는 기본 연산과 유희왕식 체인 처리(GameDesign §7.2)를 담당한다.
    /// 효과 블록은 상태를 직접 바꾸지 않고 여기의 기본 연산을 호출한다.
    ///
    /// 체인 처리 (<see cref="Advance"/>):
    /// 1. 행동이 일으킨 발동은 대기 효과(PendingTriggers)로 모인다.
    /// 2. 체인 만들기: 현재 턴 플레이어의 대기 효과부터, 그다음 상대 것. 같은 플레이어 효과가 둘 이상이거나
    ///    선택 효과가 있으면 플레이어에게 순서·발동 여부를 묻는다(ChooseTriggers).
    /// 3. 대응 기회: 상대 → 발동자 순. 대응할 효과가 없으면 자동 패스. 둘 다 패스하면 체인이 닫힌다.
    /// 4. 마지막 링크부터 처리. 처리 중 체력 0 이하는 즉시 사망. 처리 중 새로 발동한 효과는 다음 체인으로.
    /// 5. 체인도 대기 효과도 없으면 이어서 할 행동(전투 피해, 턴 넘기기)을 실행.
    /// 매 단계마다 상시 효과 재계산 → 사망 → 승패 판정.
    /// </summary>
    public sealed class RuleContext
    {
        readonly List<GameEvent> _events = new List<GameEvent>();

        public GameState State { get; }
        public EffectRegistry Registry { get; }
        public RuleConfig Config => State.Rules;
        public IReadOnlyList<GameEvent> Events => _events;

        public RuleContext(GameState state, EffectRegistry registry)
        {
            State = state ?? throw new ArgumentNullException(nameof(state));
            Registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public void Emit(GameEvent gameEvent) => _events.Add(gameEvent);

        // ------------------------------------------------------------------ 조회

        /// <summary>보드 위에 있는 카드만 찾는다.</summary>
        public Entity FindBoardEntity(int id)
        {
            var e = State.FindEntity(id);
            return e != null && e.IsOnBoard ? e : null;
        }

        /// <summary>효과 대상이 될 수 있는가: 영웅 또는 보드 위 카드. (은신은 공격만 막으므로 무관)</summary>
        public bool IsValidEffectTarget(int id) => State.FindHero(id) != null || FindBoardEntity(id) != null;

        /// <summary>대상 ID의 주인 플레이어. 이미 사라진 효과 주인 카드도 처리한다. 못 찾으면 -1.</summary>
        public int OwnerOf(int id, Entity source)
        {
            if (source != null && source.Id == id)
                return source.OwnerIndex;
            var hero = State.FindHero(id);
            if (hero != null)
                return hero.OwnerIndex;
            var e = State.FindEntity(id);
            return e != null ? e.OwnerIndex : -1;
        }

        /// <summary>카드의 효과 목록: 카드 정의의 효과, 그다음 키워드 효과 (결정적 순서).</summary>
        public void CollectEffects(Entity entity, List<EffectDefinition> results) =>
            EffectLookup.Collect(entity, Registry, results);

        // ------------------------------------------------------------------ 드로우 / 버리기

        public void DrawCards(int playerIndex, int count)
        {
            for (int i = 0; i < count; i++)
                DrawCard(playerIndex);
        }

        /// <summary>덱 맨 위에서 1장. 덱이 비면 피로 피해, 손패가 가득 차면 소멸.</summary>
        public void DrawCard(int playerIndex)
        {
            var player = State.GetPlayer(playerIndex);
            if (player.Deck.Count == 0)
            {
                if (!Config.FatigueEnabled)
                    return;
                player.FatigueCount++;
                Emit(new FatigueEvent(playerIndex, player.FatigueCount));
                DealDamage(0, player.Hero.Id, player.FatigueCount);
                return;
            }

            var card = player.Deck.Entities[0];
            player.Deck.Remove(card);
            if (!player.Hand.TryAdd(card))
            {
                player.Exile.TryAdd(card);
                Emit(new CardBurnedEvent(playerIndex, card.Id));
                return;
            }
            Emit(new CardDrawnEvent(playerIndex, card.Id));
        }

        public void DiscardRandom(int playerIndex, int count)
        {
            var player = State.GetPlayer(playerIndex);
            for (int i = 0; i < count && player.Hand.Count > 0; i++)
            {
                var card = player.Hand.Entities[State.Rng.Next(player.Hand.Count)];
                player.Hand.Remove(card);
                player.Graveyard.TryAdd(card);
                Emit(new CardDiscardedEvent(playerIndex, card.Id));
            }
        }

        // ------------------------------------------------------------------ 피해 / 회복 / 수치

        /// <summary>영웅 또는 체력이 있는 보드 위 카드(기물)에 피해. 필드·동료·소환물은 체력이 없어 무시된다.</summary>
        public void DealDamage(int sourceId, int targetId, int amount)
        {
            if (amount <= 0)
                return;

            var hero = State.FindHero(targetId);
            if (hero != null)
            {
                hero.Health -= amount;
                Emit(new DamageDealtEvent(sourceId, targetId, amount));
                return;
            }

            var e = FindBoardEntity(targetId);
            if (e == null || !e.Health.HasValue)
                return;
            e.Damage += amount;
            Emit(new DamageDealtEvent(sourceId, targetId, amount));
            CollectTriggers(e, TriggerType.OnDamaged);
            // 체력이 0 이하가 되면 그 즉시 사망한다 (GameDesign §7.2).
            if (e.IsDead)
                RemoveFromBoard(e, ExitReason.Died);
        }

        public void Heal(int targetId, int amount)
        {
            if (amount <= 0)
                return;

            var hero = State.FindHero(targetId);
            if (hero != null)
            {
                int healed = Math.Min(amount, hero.MaxHealth - hero.Health);
                if (healed <= 0)
                    return;
                hero.Health += healed;
                Emit(new HealedEvent(targetId, healed));
                return;
            }

            var e = FindBoardEntity(targetId);
            if (e == null || !e.Health.HasValue)
                return;
            int amountHealed = Math.Min(amount, e.Damage);
            if (amountHealed <= 0)
                return;
            e.Damage -= amountHealed;
            Emit(new HealedEvent(targetId, amountHealed));
        }

        public void EmitStats(Entity e)
        {
            Emit(new StatsChangedEvent(e.Id, e.Attack, e.Health, e.MaxHealth, e.Cost));
        }

        /// <summary>사용 횟수 증감. 0 이하가 되면 퇴장한다. 사용 횟수가 없는 카드(영구)는 무시.</summary>
        public void ModifyUsage(Entity e, int amount)
        {
            if (!e.Usage.HasValue || amount == 0)
                return;
            e.Usage = Math.Max(0, e.Usage.Value + amount);
            Emit(new UsageChangedEvent(e.Id, e.Usage.Value));
            if (e.Usage.Value <= 0)
                RemoveFromBoard(e, ExitReason.UsageDepleted);
        }

        // ------------------------------------------------------------------ 보드 등장 / 퇴장

        public static ZoneType BoardZoneFor(CardType type)
        {
            switch (type)
            {
                case CardType.Minion: return ZoneType.Minion;
                case CardType.Field: return ZoneType.Field;
                case CardType.Companion: return ZoneType.Companion;
                case CardType.Summon: return ZoneType.Summon;
                default: throw new ArgumentOutOfRangeException(nameof(type), type, "Card type has no board zone.");
            }
        }

        /// <summary>보드에 놓고 "등장 시" 발동을 모은다. 영역이 가득 차면 false.</summary>
        public bool TryPlaceOnBoard(Entity e, int? index, int? chosenTargetId)
        {
            var zoneType = BoardZoneFor(e.Type);
            var zone = State.GetPlayer(e.OwnerIndex).GetZone(zoneType);
            if (zone.IsFull)
                return false;

            int at = index.HasValue ? Math.Max(0, Math.Min(index.Value, zone.Count)) : zone.Count;
            zone.TryInsert(e, at);
            Emit(new CardEnteredEvent(e.Id, zoneType, at));
            CollectTriggers(e, TriggerType.OnEnter, chosenTargetId);
            return true;
        }

        /// <summary>보드에서 퇴장. 소환물은 완전히 제거되고, 나머지는 묘지로 간다. "퇴장 시" 발동을 모은다.</summary>
        public void RemoveFromBoard(Entity e, ExitReason reason)
        {
            if (!e.IsOnBoard)
                return;

            var from = e.Location.Value;
            var player = State.GetPlayer(e.OwnerIndex);
            player.GetZone(from).Remove(e);
            Emit(new CardExitedEvent(e.Id, from, reason));
            if (e.Type != CardType.Summon)
                player.Graveyard.TryAdd(e);
            CollectTriggers(e, TriggerType.OnExit);
        }

        /// <summary>
        /// 소환물 생성 (GameDesign §4.3). 사용 횟수 갱신 옵션이면 같은 소환물의 사용 횟수를 다시 채우고,
        /// 아니면 새로 놓되 칸이 가득 차면 무시한다.
        /// </summary>
        public void CreateSummon(int ownerIndex, CardDefinition definition, string summonKey)
        {
            var player = State.GetPlayer(ownerIndex);

            if (definition.OnDuplicate == SummonDuplicateMode.RefreshUsage)
            {
                var summons = player.Summons.Entities;
                for (int i = 0; i < summons.Count; i++)
                {
                    if (summons[i].SummonKey != summonKey)
                        continue;
                    summons[i].Usage = definition.Usage;
                    Emit(new UsageChangedEvent(summons[i].Id, summons[i].Usage ?? 0));
                    return;
                }
            }

            if (player.Summons.IsFull)
            {
                Emit(new PlacementFailedEvent(ownerIndex, ZoneType.Summon));
                return;
            }

            var summon = State.CreateEntity(ownerIndex, definition);
            summon.SummonKey = summonKey;
            TryPlaceOnBoard(summon, null, null);
        }

        // ------------------------------------------------------------------ 발동 수집

        /// <summary>카드의 효과 중 trigger에 해당하고 조건을 만족하는 것을 대기 효과로 모은다.</summary>
        public void CollectTriggers(Entity source, TriggerType trigger, int? chosenTargetId = null, bool isCast = false)
        {
            bool mustBeOnBoard = trigger != TriggerType.OnExit && !isCast;
            if (mustBeOnBoard && !source.IsOnBoard)
                return;

            var effects = new List<EffectDefinition>();
            CollectEffects(source, effects);
            for (int i = 0; i < effects.Count; i++)
            {
                var effect = effects[i];
                if (effect?.Trigger == null || effect.Trigger.Type != trigger || effect.Action == null)
                    continue;
                if (!ConditionsMet(new EffectContext(this, source, effect, i, chosenTargetId)))
                    continue;

                State.PendingTriggers.Add(new PendingTrigger
                {
                    Id = State.NextTriggerId++,
                    SourceId = source.Id,
                    ControllerIndex = source.OwnerIndex,
                    EffectIndex = i,
                    Trigger = trigger,
                    ChosenTargetId = chosenTargetId,
                    IsCast = isCast,
                    IsOptional = effect.Activation == EffectActivation.Optional,
                });
            }
        }

        /// <summary>플레이어 보드의 모든 카드에서 같은 시점 발동을 모은다 (보드 영역 순서, 왼쪽부터).</summary>
        public void CollectBoardTriggers(int playerIndex, TriggerType trigger)
        {
            var zones = State.GetPlayer(playerIndex).BoardZones;
            for (int z = 0; z < zones.Count; z++)
            {
                var entities = new List<Entity>(zones[z].Entities);
                for (int i = 0; i < entities.Count; i++)
                    CollectTriggers(entities[i], trigger);
            }
        }

        // ------------------------------------------------------------------ 체인 진행

        /// <summary>
        /// 선택을 기다리거나 할 일이 없을 때까지 진행한다.
        /// </summary>
        public void Advance()
        {
            while (true)
            {
                Settle();
                if (State.Phase != GamePhase.InProgress)
                {
                    ClearChainState();
                    State.PendingActions.Clear();
                    return;
                }
                if (State.Decision != null)
                    return;

                if (State.ChainClosed)
                {
                    ResolveNextLink();
                    continue;
                }
                if (State.PendingTriggers.Count > 0)
                {
                    if (!PlacePendingTriggers())
                        return;
                    continue;
                }
                if (State.Chain.Count > 0)
                {
                    if (!RunResponseWindow())
                        return;
                    continue;
                }
                if (State.PendingActions.Count > 0)
                {
                    var action = State.PendingActions[0];
                    State.PendingActions.RemoveAt(0);
                    ExecutePendingAction(action);
                    continue;
                }
                return;
            }
        }

        /// <summary>
        /// 대기 효과를 체인에 올린다. 현재 턴 플레이어 먼저. 선택이 필요하면 Decision을 걸고 false.
        /// </summary>
        bool PlacePendingTriggers()
        {
            for (int k = 0; k < GameState.PlayerCount; k++)
            {
                int player = (State.ActivePlayerIndex + k) % GameState.PlayerCount;
                var mine = PendingOf(player);
                if (mine.Count == 0)
                    continue;

                if (mine.Count == 1 && !mine[0].IsOptional)
                {
                    AddLink(mine[0]);
                    State.PendingTriggers.Remove(mine[0]);
                    k--; // 같은 플레이어를 다시 확인 (남은 것 없음)
                    continue;
                }

                var options = new List<int>();
                for (int i = 0; i < mine.Count; i++)
                    options.Add(mine[i].Id);
                State.Decision = new PendingDecision(DecisionType.ChooseTriggers, player, options);
                Emit(new DecisionRequestedEvent(player, DecisionType.ChooseTriggers, options));
                return false;
            }

            State.ResponsePasses = 0;
            return true;
        }

        List<PendingTrigger> PendingOf(int player)
        {
            var result = new List<PendingTrigger>();
            for (int i = 0; i < State.PendingTriggers.Count; i++)
            {
                if (State.PendingTriggers[i].ControllerIndex == player)
                    result.Add(State.PendingTriggers[i]);
            }
            return result;
        }

        void AddLink(PendingTrigger trigger)
        {
            var link = new ChainLink
            {
                Number = State.Chain.Count + 1,
                SourceId = trigger.SourceId,
                ControllerIndex = trigger.ControllerIndex,
                EffectIndex = trigger.EffectIndex,
                Trigger = trigger.Trigger,
                ChosenTargetId = trigger.ChosenTargetId,
                IsCast = trigger.IsCast,
            };
            State.Chain.Add(link);

            var effect = EffectOf(link.SourceId, link.EffectIndex);
            Emit(new EffectActivatedEvent(link.Number, link.SourceId, link.EffectIndex, link.ControllerIndex,
                link.Trigger, effect?.Action?.Type ?? default(ActionType)));
        }

        /// <summary>
        /// ChooseTriggers 선택 적용: 고른 순서대로 체인에 올리고, 고르지 않은 선택 효과는 버린다.
        /// 검증은 <see cref="GameEngine.Validate"/>에서 끝났다고 가정한다.
        /// </summary>
        public void ApplyTriggerChoice(int player, IReadOnlyList<int> orderedTriggerIds)
        {
            for (int i = 0; i < orderedTriggerIds.Count; i++)
            {
                var trigger = FindPending(orderedTriggerIds[i]);
                if (trigger != null)
                    AddLink(trigger);
            }
            State.PendingTriggers.RemoveAll(t => t.ControllerIndex == player);
            State.Decision = null;
        }

        public PendingTrigger FindPending(int triggerId)
        {
            for (int i = 0; i < State.PendingTriggers.Count; i++)
            {
                if (State.PendingTriggers[i].Id == triggerId)
                    return State.PendingTriggers[i];
            }
            return null;
        }

        /// <summary>대응 기회: 상대 → 발동자. 대응할 효과가 없으면 자동 패스. 선택이 필요하면 false.</summary>
        bool RunResponseWindow()
        {
            while (State.ResponsePasses < 2)
            {
                int lastController = State.Chain[State.Chain.Count - 1].ControllerIndex;
                int responder = State.ResponsePasses == 0 ? 1 - lastController : lastController;
                if (HasResponses(responder))
                {
                    State.Decision = new PendingDecision(DecisionType.ChainResponse, responder, null);
                    Emit(new DecisionRequestedEvent(responder, DecisionType.ChainResponse, State.Decision.Options));
                    return false;
                }
                State.ResponsePasses++;
            }

            State.ChainClosed = true;
            return true;
        }

        /// <summary>
        /// 체인에 대응해 발동할 수 있는 효과가 있는가.
        /// 대응용 블록(상대 카드 사용·효과 발동에 대응, 무효화)은 아직 없으므로 항상 false (GameDesign §7.2).
        /// </summary>
        public bool HasResponses(int playerIndex) => false;

        /// <summary>대응 기회에서 패스.</summary>
        public void PassChain()
        {
            State.Decision = null;
            State.ResponsePasses++;
        }

        void ResolveNextLink()
        {
            if (State.Chain.Count == 0)
            {
                State.ChainClosed = false;
                State.ResponsePasses = 0;
                Emit(new ChainEndedEvent());
                return;
            }

            if (++State.ResolutionCount > Config.MaxTriggerResolutionsPerAction)
            {
                ClearChainState();
                Emit(new ResolutionLimitReachedEvent());
                return;
            }

            var link = State.Chain[State.Chain.Count - 1];
            State.Chain.RemoveAt(State.Chain.Count - 1);
            ResolveLink(link);
        }

        void ResolveLink(ChainLink link)
        {
            var source = State.GetEntity(link.SourceId);
            var effect = EffectOf(link.SourceId, link.EffectIndex);
            if (source == null || effect == null)
                return;

            if (link.Negated)
            {
                Emit(new EffectNegatedEvent(link.Number, link.SourceId));
                return;
            }

            Emit(new EffectResolvingEvent(link.Number, link.SourceId, link.EffectIndex, effect.Action.Type));
            var context = new EffectContext(this, source, effect, link.EffectIndex, link.ChosenTargetId);
            Execute(context);

            // 반복 발동(턴 시작/종료, 피격, 공격, 카드 사용 시)은 처리될 때마다 사용 횟수를 1 소모한다.
            if (ConsumesUsage(link.Trigger) && source.Usage.HasValue && source.IsOnBoard)
                ModifyUsage(source, -1);
        }

        EffectDefinition EffectOf(int sourceId, int effectIndex)
        {
            var source = State.GetEntity(sourceId);
            if (source == null)
                return null;
            var effects = new List<EffectDefinition>();
            CollectEffects(source, effects);
            return effectIndex >= 0 && effectIndex < effects.Count ? effects[effectIndex] : null;
        }

        void ClearChainState()
        {
            State.Chain.Clear();
            State.PendingTriggers.Clear();
            State.ChainClosed = false;
            State.ResponsePasses = 0;
            State.Decision = null;
        }

        static bool ConsumesUsage(TriggerType trigger)
        {
            return trigger != TriggerType.OnEnter && trigger != TriggerType.OnExit && trigger != TriggerType.Aura;
        }

        bool ConditionsMet(EffectContext context)
        {
            var conditions = context.Effect.Conditions;
            if (conditions == null)
                return true;
            for (int i = 0; i < conditions.Count; i++)
            {
                if (conditions[i] != null && !Registry.GetCondition(conditions[i].Type).Evaluate(context, conditions[i]))
                    return false;
            }
            return true;
        }

        void Execute(EffectContext context)
        {
            var action = Registry.GetAction(context.Effect.Action.Type);
            var targets = new List<int>();
            if (action.UsesTargets && context.Effect.Target != null)
                Registry.GetTarget(context.Effect.Target.Type).Select(context, context.Effect.Target, targets);
            action.Execute(context, targets);
        }

        // ------------------------------------------------------------------ 여러 단계 행동

        void ExecutePendingAction(PendingAction action)
        {
            switch (action.Type)
            {
                case PendingActionType.CombatDamage:
                    CombatDamage(action.A, action.B);
                    break;
                case PendingActionType.FinishEndTurn:
                    Emit(new TurnEndedEvent(action.A));
                    StartTurn(1 - action.A);
                    break;
            }
        }

        /// <summary>공격자와 대상이 서로 공격력만큼 동시에 피해 (영웅은 반격 없음).</summary>
        void CombatDamage(int attackerId, int targetId)
        {
            var attacker = FindBoardEntity(attackerId);
            if (attacker == null)
                return;

            bool targetIsHero = State.FindHero(targetId) != null;
            var targetMinion = targetIsHero ? null : FindBoardEntity(targetId);
            if (!targetIsHero && targetMinion == null)
                return;

            int attackerDamage = attacker.Attack ?? 0;
            int counterDamage = targetMinion?.Attack ?? 0;
            DealDamage(attacker.Id, targetId, attackerDamage);
            if (targetMinion != null)
                DealDamage(targetMinion.Id, attacker.Id, counterDamage);
        }

        /// <summary>턴 시작: 최대 코스트 증가·충전 → 기물 공격 가능 상태로 → 1장 드로우 → "턴 시작 시" 발동 수집.</summary>
        public void StartTurn(int playerIndex)
        {
            var rules = Config;
            var player = State.GetPlayer(playerIndex);

            State.ActivePlayerIndex = playerIndex;
            State.TurnNumber++;

            int nextMax = player.TurnsTaken == 0 ? rules.StartingMaxCost : player.MaxCost + rules.MaxCostGainPerTurn;
            player.MaxCost = Math.Min(nextMax, rules.MaxCostLimit);
            player.AvailableCost = player.MaxCost;
            player.TurnsTaken++;

            var minions = player.Minions.Entities;
            for (int i = 0; i < minions.Count; i++)
            {
                minions[i].PlayedThisTurn = false;
                minions[i].AttacksThisTurn = 0;
            }

            Emit(new TurnStartedEvent(playerIndex, State.TurnNumber));
            Emit(new CostChangedEvent(playerIndex, player.MaxCost, player.AvailableCost));

            DrawCard(playerIndex);
            CollectBoardTriggers(playerIndex, TriggerType.OnTurnStart);
        }

        // ------------------------------------------------------------------ 상시 효과 / 사망 / 승패

        /// <summary>상시(Aura) 보정값을 처음부터 다시 계산하고, 바뀐 카드는 StatsChanged를 낸다.</summary>
        public void RecalculateAuras()
        {
            var all = new List<Entity>();
            for (int p = 0; p < GameState.PlayerCount; p++)
            {
                var zones = State.GetPlayer(p).Zones;
                for (int z = 0; z < zones.Count; z++)
                    all.AddRange(zones[z].Entities);
            }

            var before = new (int? attack, int? health, int cost)[all.Count];
            for (int i = 0; i < all.Count; i++)
            {
                before[i] = (all[i].Attack, all[i].Health, all[i].Cost);
                all[i].AuraAttack = 0;
                all[i].AuraHealth = 0;
                all[i].AuraCost = 0;
            }

            var effects = new List<EffectDefinition>();
            var targets = new List<int>();
            for (int p = 0; p < GameState.PlayerCount; p++)
            {
                var zones = State.GetPlayer(p).BoardZones;
                for (int z = 0; z < zones.Count; z++)
                {
                    var entities = zones[z].Entities;
                    for (int i = 0; i < entities.Count; i++)
                    {
                        var source = entities[i];
                        effects.Clear();
                        CollectEffects(source, effects);
                        for (int k = 0; k < effects.Count; k++)
                            ApplyAura(source, effects[k], k, targets);
                    }
                }
            }

            for (int i = 0; i < all.Count; i++)
            {
                var e = all[i];
                if (before[i].attack != e.Attack || before[i].health != e.Health || before[i].cost != e.Cost)
                    EmitStats(e);
            }
        }

        void ApplyAura(Entity source, EffectDefinition effect, int index, List<int> targets)
        {
            if (effect?.Trigger == null || effect.Trigger.Type != TriggerType.Aura || effect.Action == null || effect.Target == null)
                return;
            if (!(Registry.GetAction(effect.Action.Type) is IAuraActionHandler aura))
                return;
            var selector = Registry.GetTarget(effect.Target.Type);
            if (selector.IsRandom || selector.RequiresChoice)
                return;

            var context = new EffectContext(this, source, effect, index, null);
            if (!ConditionsMet(context))
                return;

            targets.Clear();
            selector.Select(context, effect.Target, targets);
            aura.ApplyAura(context, targets);
        }

        /// <summary>상시 효과 재계산 → 사망 → 승패 판정을 더 바뀌는 것이 없을 때까지 반복한다.</summary>
        public void Settle()
        {
            do
            {
                RecalculateAuras();
            }
            while (ProcessDeaths());
            CheckGameOver();
        }

        /// <summary>체력 0 이하 기물을 퇴장시킨다. 한 명이라도 처리했으면 true.</summary>
        public bool ProcessDeaths()
        {
            var dead = new List<Entity>();
            for (int k = 0; k < GameState.PlayerCount; k++)
            {
                var minions = State.GetPlayer((State.ActivePlayerIndex + k) % GameState.PlayerCount).Minions.Entities;
                for (int i = 0; i < minions.Count; i++)
                {
                    if (minions[i].IsDead)
                        dead.Add(minions[i]);
                }
            }

            for (int i = 0; i < dead.Count; i++)
                RemoveFromBoard(dead[i], ExitReason.Died);
            return dead.Count > 0;
        }

        /// <summary>영웅 체력 0 이하면 대전 종료. 둘 다면 무승부.</summary>
        public void CheckGameOver()
        {
            if (State.Phase != GamePhase.InProgress)
                return;

            bool dead0 = State.GetPlayer(0).Hero.Health <= 0;
            bool dead1 = State.GetPlayer(1).Hero.Health <= 0;
            if (!dead0 && !dead1)
                return;

            State.Phase = GamePhase.Finished;
            State.WinnerIndex = dead0 && dead1 ? (int?)null : (dead0 ? 1 : 0);
            Emit(new GameEndedEvent(State.WinnerIndex));
        }
    }
}
