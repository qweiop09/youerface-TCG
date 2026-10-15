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
    /// 체인 처리 (<see cref="Advance"/>, GameDesign §7.2):
    /// 1. 행동이 일으킨 발동은 대기 효과(PendingTriggers)로 모인다.
    /// 2. 체인 기회는 현재 턴 플레이어부터. 기회를 가진 플레이어는 대기 효과 하나를 올리거나 패스한다 (한 번에 하나).
    ///    체인 1 다음은 발동자 → 상대, 체인 2 이후 다음은 상대 → 건 사람. 둘이 연속 패스하면 체인이 닫힌다.
    ///    올리지 않은 강제 효과가 있으면 패스할 수 없고, 강제 1개뿐이면 자동으로 올린다. 올릴 것이 없으면 자동 패스.
    /// 3. 마지막 링크부터 처리. 처리 중 체력 0 이하는 즉시 사망. 처리 중 새로 발동한 효과는 다음 체인으로.
    /// 4. 체인도 대기 효과도 없으면 이어서 할 행동(전투 피해, 턴 넘기기)을 실행.
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
                DealDamage(0, player.Hero.Id, player.FatigueCount, DamageType.Fatigue);
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

        /// <summary>
        /// 영웅 또는 체력이 있는 보드 위 카드(기물)에 피해. 필드·동료·소환물은 체력이 없어 무시된다.
        /// 체력이 0 이하가 되면 그 즉시 사망한다 (GameDesign §7.2).
        /// </summary>
        public void DealDamage(int sourceId, int targetId, int amount, DamageType type)
        {
            var hit = ApplyDamage(sourceId, targetId, amount, type);
            if (hit != null && hit.IsDead)
                RemoveFromBoard(hit, ExitReason.Died);
        }

        /// <summary>
        /// 피해만 적용하고 피해 트리거를 모은다 (사망 처리는 호출한 쪽). 맞은 기물을 돌려준다(영웅·무시되면 null).
        /// 트리거 수집 순서 (GameDesign §7.6): 받은 쪽의 "받을 때" → 준 쪽(보드 위 카드)의 "줄 때".
        /// 피로 피해는 "모든 피해를 받을 때"에만 걸리고 주는 쪽이 없다.
        /// </summary>
        Entity ApplyDamage(int sourceId, int targetId, int amount, DamageType type)
        {
            if (amount <= 0)
                return null;

            Entity hit = null;
            var hero = State.FindHero(targetId);
            if (hero != null)
            {
                hero.Health -= amount;
            }
            else
            {
                hit = FindBoardEntity(targetId);
                if (hit == null || !hit.Health.HasValue)
                    return null;
                hit.Damage += amount;
            }
            Emit(new DamageDealtEvent(sourceId, targetId, amount, type));

            // 영웅은 아직 효과를 갖지 않으므로 받는 쪽 트리거는 기물만.
            if (hit != null)
                CollectTriggers(hit, TakenTrigger(type), TriggerType.OnAnyDamageTaken);

            if (type != DamageType.Fatigue && sourceId != 0)
            {
                var source = FindBoardEntity(sourceId);
                if (source != null)
                    CollectTriggers(source, DealtTrigger(type), TriggerType.OnAnyDamageDealt);
            }
            return hit;
        }

        /// <summary>피해 유형별 "받을 때" 트리거. 피로는 유형 전용 트리거가 없다(null).</summary>
        static TriggerType? TakenTrigger(DamageType type)
        {
            switch (type)
            {
                case DamageType.Effect: return TriggerType.OnEffectDamageTaken;
                case DamageType.Combat: return TriggerType.OnCombatDamageTaken;
                default: return null;
            }
        }

        static TriggerType? DealtTrigger(DamageType type)
        {
            switch (type)
            {
                case DamageType.Effect: return TriggerType.OnEffectDamageDealt;
                case DamageType.Combat: return TriggerType.OnCombatDamageDealt;
                default: return null;
            }
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

        /// <summary>보드에 놓고 "등장 시" 발동을 모은다. 영역이 가득 차면 false. index: 기물은 칸 번호, 그 밖은 목록 순번.</summary>
        public bool TryPlaceOnBoard(Entity e, int? index, int? chosenTargetId)
        {
            var zoneType = BoardZoneFor(e.Type);
            var zone = State.GetPlayer(e.OwnerIndex).GetZone(zoneType);
            if (zone.IsFull)
                return false;

            int at;
            if (zone.UsesSlots)
            {
                // 기물: 고른 칸(비어 있을 때) → 아니면 가운데에 가까운 빈 칸. 기존 기물은 밀리지 않는다.
                e.Slot = -1;
                if (!(index.HasValue && zone.TryPlaceAt(e, index.Value)) && !zone.TryAdd(e))
                    return false;
                at = zone.IndexOf(e);
            }
            else
            {
                at = index.HasValue ? Math.Max(0, Math.Min(index.Value, zone.Count)) : zone.Count;
                zone.TryInsert(e, at);
            }
            Emit(new CardEnteredEvent(e.Id, zoneType, at, e.Slot));
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
        public void CollectTriggers(Entity source, TriggerType trigger, int? chosenTargetId = null, bool isCast = false) =>
            CollectTriggers(source, trigger, null, chosenTargetId, isCast);

        /// <summary>
        /// 두 트리거 중 하나에 해당하는 효과를 효과 순번대로 한 번에 모은다 (예: "전투 피해를 받을 때" + "모든 피해를 받을 때").
        /// first가 null이면 second만.
        /// </summary>
        public void CollectTriggers(Entity source, TriggerType? first, TriggerType? second, int? chosenTargetId = null, bool isCast = false)
        {
            bool mustBeOnBoard = first != TriggerType.OnExit && !isCast;
            if (mustBeOnBoard && !source.IsOnBoard)
                return;

            var effects = new List<EffectDefinition>();
            CollectEffects(source, effects);
            for (int i = 0; i < effects.Count; i++)
            {
                var effect = effects[i];
                if (effect?.Trigger == null || effect.Action == null)
                    continue;
                var type = effect.Trigger.Type;
                if (type != first && type != second)
                    continue;
                if (!ConditionsMet(new EffectContext(this, source, effect, i, chosenTargetId)))
                    continue;

                State.PendingTriggers.Add(new PendingTrigger
                {
                    Id = State.NextTriggerId++,
                    SourceId = source.Id,
                    ControllerIndex = source.OwnerIndex,
                    EffectIndex = i,
                    Trigger = type,
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
                if (State.Chain.Count > 0 || State.PendingTriggers.Count > 0)
                {
                    if (!RunPriority())
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
        /// 체인 기회 1번을 처리한다: 자동으로 올리거나 자동 패스하면 true, 플레이어 선택이 필요하면 Decision을 걸고 false.
        /// </summary>
        bool RunPriority()
        {
            if (State.PriorityPlayer < 0)
            {
                int active = State.ActivePlayerIndex;
                State.PriorityPlayer = PendingOf(active).Count > 0 ? active : 1 - active;
                State.PriorityPasses = 0;
            }

            int player = State.PriorityPlayer;
            var mine = PendingOf(player);
            bool hasMandatory = mine.Exists(t => !t.IsOptional);

            // 대응 효과(HasResponses)가 생기면 여기서 선택지에 더한다.
            if (mine.Count == 0 && !HasResponses(player))
            {
                Pass(player);
                return true;
            }
            if (mine.Count == 1 && hasMandatory && !HasResponses(player))
            {
                Place(mine[0]);
                return true;
            }

            var options = new List<int>();
            for (int i = 0; i < mine.Count; i++)
                options.Add(mine[i].Id);
            State.Decision = new PendingDecision(DecisionType.ChainPriority, player, options, !hasMandatory);
            Emit(new DecisionRequestedEvent(player, DecisionType.ChainPriority, options));
            return false;
        }

        /// <summary>대기 효과를 체인에 올리고 다음 체인 기회를 정한다.</summary>
        void Place(PendingTrigger trigger)
        {
            AddLink(trigger);
            State.PendingTriggers.Remove(trigger);
            State.PriorityPasses = 0;
            int number = State.Chain[State.Chain.Count - 1].Number;
            // 처음 발동한 효과(체인 1) 다음은 발동자부터, 체인으로 건 효과 다음은 상대부터.
            State.PriorityPlayer = number == 1 ? trigger.ControllerIndex : 1 - trigger.ControllerIndex;
        }

        void Pass(int player)
        {
            State.PriorityPasses++;
            if (State.PriorityPasses < 2)
            {
                State.PriorityPlayer = 1 - player;
                return;
            }

            // 둘 다 패스: 체인이 닫힌다. 남은 대기 효과는 (강제는 남을 수 없으므로) 발동하지 않은 선택 효과뿐이다.
            State.PendingTriggers.Clear();
            State.PriorityPlayer = -1;
            State.PriorityPasses = 0;
            if (State.Chain.Count > 0)
                State.ChainClosed = true;
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

        /// <summary>체인 기회 선택: 대기 효과 하나를 올린다. 검증은 <see cref="GameEngine.Validate"/>에서 끝났다고 가정한다.</summary>
        public void ActivateFromDecision(int triggerId)
        {
            State.Decision = null;
            var trigger = FindPending(triggerId);
            if (trigger != null)
                Place(trigger);
        }

        /// <summary>체인 기회 선택: 패스.</summary>
        public void PassChain()
        {
            int player = State.Decision?.PlayerIndex ?? State.PriorityPlayer;
            State.Decision = null;
            Pass(player);
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

        /// <summary>
        /// 체인에 대응해 발동할 수 있는 효과가 있는가.
        /// 대응용 블록(상대 카드 사용·효과 발동에 대응, 무효화)은 아직 없으므로 항상 false (GameDesign §7.2).
        /// </summary>
        public bool HasResponses(int playerIndex) => false;

        void ResolveNextLink()
        {
            if (State.Chain.Count == 0)
            {
                State.ChainClosed = false;
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

            // 반복 발동(턴 시작/종료, 피해 받을·줄 때, 공격, 카드 사용 시)은 처리될 때마다 사용 횟수를 1 소모한다.
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
            State.PriorityPlayer = -1;
            State.PriorityPasses = 0;
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

        /// <summary>
        /// 공격자와 대상이 서로 공격력만큼 동시에 전투 피해 (영웅은 반격 없음).
        /// 두 피해를 모두 적용하고 피해 트리거를 모은 뒤 사망을 처리한다 — 반격한 기물이 공격으로 쓰러져도 "줄 때"가 걸린다.
        /// </summary>
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
            var hitTarget = ApplyDamage(attacker.Id, targetId, attackerDamage, DamageType.Combat);
            var hitAttacker = targetMinion != null ? ApplyDamage(targetMinion.Id, attacker.Id, counterDamage, DamageType.Combat) : null;

            if (hitTarget != null && hitTarget.IsDead)
                RemoveFromBoard(hitTarget, ExitReason.Died);
            if (hitAttacker != null && hitAttacker.IsDead)
                RemoveFromBoard(hitAttacker, ExitReason.Died);
        }

        /// <summary>턴 시작: 최대 코스트 증가·충전 → 기물 공격 가능 상태로 → 1장 드로우 → "턴 시작 시" 발동 수집.</summary>
        public void StartTurn(int playerIndex)
        {
            var rules = Config;
            var player = State.GetPlayer(playerIndex);

            State.ActivePlayerIndex = playerIndex;
            State.TurnNumber++;

            // 첫 턴: 상대가 이미 턴을 가졌으면 후공 — 대전 시작 때 받은 코스트 그대로, 늘지 않음 (GameDesign §11.3).
            int nextMax = player.TurnsTaken > 0 ? player.MaxCost + rules.MaxCostGainPerTurn
                : State.GetOpponent(playerIndex).TurnsTaken > 0 ? rules.SecondPlayerStartingMaxCost
                : rules.StartingMaxCost;
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
                all[i].ClearAuraChanges();
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

        /// <summary>항복: 진행 중인 체인·선택을 버리고 상대 승리로 끝낸다.</summary>
        public void Concede(int playerIndex)
        {
            if (State.Phase != GamePhase.InProgress)
                return;
            State.Decision = null;
            State.Chain.Clear();
            State.ChainClosed = false;
            State.PendingTriggers.Clear();
            State.PendingActions.Clear();
            State.PriorityPlayer = -1;
            State.PriorityPasses = 0;
            State.Phase = GamePhase.Finished;
            State.WinnerIndex = 1 - playerIndex;
            Emit(new GameEndedEvent(State.WinnerIndex));
        }
    }
}
