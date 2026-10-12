using System;
using System.Collections.Generic;
using TCG.Core.Data;
using TCG.Core.Effects;
using TCG.Core.Events;
using TCG.Core.State;

namespace TCG.Core.Rules
{
    /// <summary>
    /// 명령 1개를 처리하는 동안의 작업 공간. 상태를 바꾸는 기본 연산, 효과 발동 대기열, 상시 효과·사망·승패 처리를 담당한다.
    /// 효과 블록은 상태를 직접 바꾸지 않고 여기의 기본 연산을 호출한다.
    ///
    /// 처리 순서 (GameDesign/Architecture에 문서화):
    /// 1. 명령이 일으킨 효과 발동은 대기열(FIFO)에 쌓이고 하나씩 처리된다.
    /// 2. 같은 시점에 여러 카드가 발동하면: 해당 플레이어의 보드 영역 순서(기물→필드→동료→소환물), 영역 안에서는 왼쪽부터.
    /// 3. 한 카드 안에서는 카드 효과 목록 순서, 그다음 키워드 효과 순서.
    /// 4. 발동 1건을 처리할 때마다 상시 효과 재계산 → 사망 처리(현재 턴 플레이어 쪽부터, 왼쪽부터) → 승패 판정.
    /// </summary>
    public sealed class RuleContext
    {
        sealed class PendingStep
        {
            public Entity Source;
            public TriggerType Trigger;
            public int? ChosenTargetId;
            /// <summary>주문 발동: 보드 위에 없어도 처리한다.</summary>
            public bool IsCast;
        }

        readonly List<GameEvent> _events = new List<GameEvent>();
        readonly Queue<PendingStep> _queue = new Queue<PendingStep>();
        int _resolutions;

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
        public void CollectEffects(Entity entity, List<EffectDefinition> results)
        {
            var own = entity.Definition.Effects;
            if (own != null)
            {
                for (int i = 0; i < own.Count; i++)
                    results.Add(own[i]);
            }

            for (int k = 0; k < entity.Keywords.Count; k++)
            {
                if (!Registry.TryGetKeyword(entity.Keywords[k], out var info))
                    continue;
                for (int i = 0; i < info.ImplicitEffects.Count; i++)
                    results.Add(info.ImplicitEffects[i]);
            }
        }

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
            QueueTrigger(e, TriggerType.OnDamaged);
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

        /// <summary>보드에 놓고 "등장 시" 발동을 대기열에 넣는다. 영역이 가득 차면 false.</summary>
        public bool TryPlaceOnBoard(Entity e, int? index, int? chosenTargetId)
        {
            var zoneType = BoardZoneFor(e.Type);
            var zone = State.GetPlayer(e.OwnerIndex).GetZone(zoneType);
            if (zone.IsFull)
                return false;

            int at = index.HasValue ? Math.Max(0, Math.Min(index.Value, zone.Count)) : zone.Count;
            zone.TryInsert(e, at);
            Emit(new CardEnteredEvent(e.Id, zoneType, at));
            QueueTrigger(e, TriggerType.OnEnter, chosenTargetId);
            return true;
        }

        /// <summary>보드에서 퇴장. 소환물은 완전히 제거되고, 나머지는 묘지로 간다. "퇴장 시" 발동을 대기열에 넣는다.</summary>
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
            QueueTrigger(e, TriggerType.OnExit);
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

        // ------------------------------------------------------------------ 발동 대기열

        public void QueueTrigger(Entity source, TriggerType trigger, int? chosenTargetId = null)
        {
            _queue.Enqueue(new PendingStep { Source = source, Trigger = trigger, ChosenTargetId = chosenTargetId });
        }

        /// <summary>주문 발동(등장 시 효과)을 대기열에 넣는다.</summary>
        public void QueueCast(Entity spell, int? chosenTargetId)
        {
            _queue.Enqueue(new PendingStep { Source = spell, Trigger = TriggerType.OnEnter, ChosenTargetId = chosenTargetId, IsCast = true });
        }

        /// <summary>플레이어 보드의 모든 카드에 같은 시점 발동을 대기열에 넣는다 (보드 영역 순서, 왼쪽부터).</summary>
        public void QueueBoardTrigger(int playerIndex, TriggerType trigger)
        {
            var zones = State.GetPlayer(playerIndex).BoardZones;
            for (int z = 0; z < zones.Count; z++)
            {
                var entities = zones[z].Entities;
                for (int i = 0; i < entities.Count; i++)
                    QueueTrigger(entities[i], trigger);
            }
        }

        /// <summary>대기열이 빌 때까지 처리한다. 매 단계마다 상시 효과 재계산 → 사망 → 승패 판정.</summary>
        public void ResolveAll()
        {
            while (true)
            {
                RecalculateAuras();
                if (ProcessDeaths())
                    continue;

                CheckGameOver();
                if (State.Phase == GamePhase.Finished)
                {
                    _queue.Clear();
                    return;
                }

                if (_queue.Count == 0)
                    return;

                if (++_resolutions > Config.MaxTriggerResolutionsPerAction)
                {
                    _queue.Clear();
                    Emit(new ResolutionLimitReachedEvent());
                    return;
                }

                ResolveStep(_queue.Dequeue());
            }
        }

        void ResolveStep(PendingStep step)
        {
            var source = step.Source;
            bool mustBeOnBoard = step.Trigger != TriggerType.OnExit && !step.IsCast;
            if (mustBeOnBoard && !source.IsOnBoard)
                return;

            var effects = new List<EffectDefinition>();
            CollectEffects(source, effects);

            bool activated = false;
            for (int i = 0; i < effects.Count; i++)
            {
                var effect = effects[i];
                if (effect?.Trigger == null || effect.Trigger.Type != step.Trigger || effect.Action == null)
                    continue;

                var context = new EffectContext(this, source, effect, i, step.ChosenTargetId);
                if (!ConditionsMet(context))
                    continue;

                activated = true;
                Execute(context, step.Trigger);
            }

            // 반복 발동(턴 시작/종료, 피격 등)은 사용 횟수를 1 소모한다. 등장·퇴장·상시는 소모하지 않는다.
            if (activated && ConsumesUsage(step.Trigger) && source.Usage.HasValue && source.IsOnBoard)
                ModifyUsage(source, -1);
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

        void Execute(EffectContext context, TriggerType trigger)
        {
            var action = Registry.GetAction(context.Effect.Action.Type);
            var targets = new List<int>();
            if (action.UsesTargets && context.Effect.Target != null)
                Registry.GetTarget(context.Effect.Target.Type).Select(context, context.Effect.Target, targets);

            Emit(new EffectTriggeredEvent(context.Source.Id, trigger, context.Effect.Action.Type));
            action.Execute(context, targets);
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
