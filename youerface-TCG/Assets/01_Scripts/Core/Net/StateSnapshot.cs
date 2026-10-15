using System;
using System.Collections.Generic;
using TCG.Core.Data;
using TCG.Core.State;

namespace TCG.Core.Net
{
    /// <summary>
    /// 시야 상태(<see cref="PlayerView.Create"/>)를 보내기 위한 직렬화 형태. 카드 정의는 <see cref="MatchCatalog"/> 번호로,
    /// 가려진 카드는 -1. 난수 상태는 담지 않는다.
    /// </summary>
    public sealed class StateSnapshot
    {
        public GamePhase Phase { get; set; }
        public int ActivePlayer { get; set; }
        public int TurnNumber { get; set; }
        public int? Winner { get; set; }
        public int NextEntityId { get; set; }
        public int NextTriggerId { get; set; }
        public int ResolutionCount { get; set; }
        public List<PlayerSnapshot> Players { get; set; } = new List<PlayerSnapshot>();
        /// <summary>어느 영역에도 없지만 표에 남은 카드 (사라진 소환물 등).</summary>
        public List<EntitySnapshot> Limbo { get; set; } = new List<EntitySnapshot>();
        public List<ChainLink> Chain { get; set; } = new List<ChainLink>();
        public bool ChainClosed { get; set; }
        public int PriorityPlayer { get; set; } = -1;
        public int PriorityPasses { get; set; }
        public List<PendingTrigger> PendingTriggers { get; set; } = new List<PendingTrigger>();
        public DecisionSnapshot Decision { get; set; }
        public List<PendingActionSnapshot> PendingActions { get; set; } = new List<PendingActionSnapshot>();

        /// <summary>시야 상태를 담는다. 목록에 없는 카드 정의는 catalog에 추가된다(새로 추가된 것은 따로 보내야 함).</summary>
        public static StateSnapshot Capture(GameState view, MatchCatalog catalog)
        {
            var s = new StateSnapshot
            {
                Phase = view.Phase,
                ActivePlayer = view.ActivePlayerIndex,
                TurnNumber = view.TurnNumber,
                Winner = view.WinnerIndex,
                NextEntityId = view.NextEntityId,
                NextTriggerId = view.NextTriggerId,
                ResolutionCount = view.ResolutionCount,
                ChainClosed = view.ChainClosed,
                PriorityPlayer = view.PriorityPlayer,
                PriorityPasses = view.PriorityPasses,
            };
            for (int p = 0; p < GameState.PlayerCount; p++)
            {
                var player = view.GetPlayer(p);
                var ps = new PlayerSnapshot
                {
                    HeroHealth = player.Hero.Health,
                    HeroMaxHealth = player.Hero.MaxHealth,
                    MaxCost = player.MaxCost,
                    AvailableCost = player.AvailableCost,
                    FatigueCount = player.FatigueCount,
                    TurnsTaken = player.TurnsTaken,
                };
                for (int z = 0; z < player.Zones.Count; z++)
                {
                    var list = new List<EntitySnapshot>();
                    var entities = player.Zones[z].Entities;
                    for (int i = 0; i < entities.Count; i++)
                        list.Add(EntitySnapshot.Capture(entities[i], catalog));
                    ps.Zones.Add(list);
                }
                s.Players.Add(ps);
            }
            for (int id = 1; id < view.NextEntityId; id++)
            {
                var e = view.GetEntity(id);
                if (e != null && !e.Location.HasValue)
                    s.Limbo.Add(EntitySnapshot.Capture(e, catalog));
            }
            for (int i = 0; i < view.Chain.Count; i++)
                s.Chain.Add(view.Chain[i].Clone());
            for (int i = 0; i < view.PendingTriggers.Count; i++)
                s.PendingTriggers.Add(view.PendingTriggers[i].Clone());
            if (view.Decision != null)
            {
                s.Decision = new DecisionSnapshot
                {
                    Type = view.Decision.Type,
                    PlayerIndex = view.Decision.PlayerIndex,
                    Options = new List<int>(view.Decision.Options),
                    CanPass = view.Decision.CanPass,
                };
            }
            for (int i = 0; i < view.PendingActions.Count; i++)
            {
                var a = view.PendingActions[i];
                s.PendingActions.Add(new PendingActionSnapshot { Type = a.Type, A = a.A, B = a.B });
            }
            return s;
        }

        /// <summary>받은 쪽에서 상태로 되돌린다. 화면 표시용이며 난수는 0 시드.</summary>
        public GameState ToState(RuleConfig rules, IReadOnlyList<CardDefinition> catalog)
        {
            var state = new GameState(rules, 0);
            if (Players.Count != GameState.PlayerCount)
                throw new FormatException("Snapshot needs two players.");
            for (int p = 0; p < GameState.PlayerCount; p++)
            {
                var ps = Players[p];
                var player = state.GetPlayer(p);
                player.Hero.MaxHealth = ps.HeroMaxHealth;
                player.Hero.Health = ps.HeroHealth;
                player.MaxCost = ps.MaxCost;
                player.AvailableCost = ps.AvailableCost;
                player.FatigueCount = ps.FatigueCount;
                player.TurnsTaken = ps.TurnsTaken;
                for (int z = 0; z < ps.Zones.Count && z < player.Zones.Count; z++)
                {
                    var zone = player.Zones[z];
                    var list = ps.Zones[z];
                    for (int i = 0; i < list.Count; i++)
                    {
                        var e = list[i].ToEntity(catalog);
                        state.PutEntity(e);
                        if (!zone.TryAdd(e))
                            throw new FormatException($"Zone {zone.Type} overflow in snapshot.");
                    }
                }
            }
            for (int i = 0; i < Limbo.Count; i++)
                state.PutEntity(Limbo[i].ToEntity(catalog));
            state.SetNextEntityId(Math.Max(NextEntityId, state.NextEntityId));

            state.Phase = Phase;
            state.ActivePlayerIndex = ActivePlayer;
            state.TurnNumber = TurnNumber;
            state.WinnerIndex = Winner;
            state.NextTriggerId = NextTriggerId;
            state.ResolutionCount = ResolutionCount;
            state.ChainClosed = ChainClosed;
            state.PriorityPlayer = PriorityPlayer;
            state.PriorityPasses = PriorityPasses;
            for (int i = 0; i < Chain.Count; i++)
                state.Chain.Add(Chain[i].Clone());
            for (int i = 0; i < PendingTriggers.Count; i++)
                state.PendingTriggers.Add(PendingTriggers[i].Clone());
            if (Decision != null)
                state.Decision = new PendingDecision(Decision.Type, Decision.PlayerIndex, Decision.Options ?? new List<int>(), Decision.CanPass);
            for (int i = 0; i < PendingActions.Count; i++)
                state.PendingActions.Add(new PendingAction(PendingActions[i].Type, PendingActions[i].A, PendingActions[i].B));
            return state;
        }
    }

    public sealed class PlayerSnapshot
    {
        public int HeroHealth { get; set; }
        public int HeroMaxHealth { get; set; }
        public int MaxCost { get; set; }
        public int AvailableCost { get; set; }
        public int FatigueCount { get; set; }
        public int TurnsTaken { get; set; }
        /// <summary><see cref="PlayerState.Zones"/> 순서의 영역별 카드.</summary>
        public List<List<EntitySnapshot>> Zones { get; set; } = new List<List<EntitySnapshot>>();
    }

    public sealed class EntitySnapshot
    {
        public int Id { get; set; }
        public int Owner { get; set; }
        /// <summary><see cref="MatchCatalog"/> 번호. -1이면 가려진 카드(나머지 값 없음).</summary>
        public int Def { get; set; } = -1;
        public int BaseCost { get; set; }
        public int AuraCost { get; set; }
        public int? BaseAttack { get; set; }
        public int AuraAttack { get; set; }
        public int? BaseMaxHealth { get; set; }
        public int AuraHealth { get; set; }
        public int Damage { get; set; }
        public int? Usage { get; set; }
        public List<Keyword> Keywords { get; set; }
        public bool Played { get; set; }
        public int Attacks { get; set; }
        public string SummonKey { get; set; }
        /// <summary>기물 칸 번호 (그 밖 -1).</summary>
        public int Slot { get; set; } = -1;
        /// <summary>능력치 변화 기록 (표시용).</summary>
        public List<StatChange> Changes { get; set; }

        public static EntitySnapshot Capture(Entity e, MatchCatalog catalog)
        {
            if (HiddenCard.Is(e))
                return new EntitySnapshot { Id = e.Id, Owner = e.OwnerIndex, Def = -1, Slot = e.Slot };
            return new EntitySnapshot
            {
                Id = e.Id,
                Owner = e.OwnerIndex,
                Def = catalog.Add(e.Definition),
                BaseCost = e.BaseCost,
                AuraCost = e.AuraCost,
                BaseAttack = e.BaseAttack,
                AuraAttack = e.AuraAttack,
                BaseMaxHealth = e.BaseMaxHealth,
                AuraHealth = e.AuraHealth,
                Damage = e.Damage,
                Usage = e.Usage,
                Keywords = new List<Keyword>(e.Keywords),
                Played = e.PlayedThisTurn,
                Attacks = e.AttacksThisTurn,
                SummonKey = e.SummonKey,
                Slot = e.Slot,
                Changes = e.Changes.Count > 0 ? e.Changes.ConvertAll(c => c.Clone()) : null,
            };
        }

        public Entity ToEntity(IReadOnlyList<CardDefinition> catalog)
        {
            if (Def < 0)
                return new Entity(Id, Owner, HiddenCard.Definition) { Slot = Slot };
            if (Def >= catalog.Count)
                throw new FormatException($"Unknown card definition #{Def}.");
            var e = new Entity(Id, Owner, catalog[Def])
            {
                BaseCost = BaseCost,
                AuraCost = AuraCost,
                BaseAttack = BaseAttack,
                AuraAttack = AuraAttack,
                BaseMaxHealth = BaseMaxHealth,
                AuraHealth = AuraHealth,
                Damage = Damage,
                Usage = Usage,
                PlayedThisTurn = Played,
                AttacksThisTurn = Attacks,
                SummonKey = SummonKey,
                Slot = Slot,
            };
            e.Keywords.Clear();
            if (Keywords != null)
                e.Keywords.AddRange(Keywords);
            if (Changes != null)
                e.Changes.AddRange(Changes.ConvertAll(c => c.Clone()));
            return e;
        }
    }

    public sealed class DecisionSnapshot
    {
        public DecisionType Type { get; set; }
        public int PlayerIndex { get; set; }
        public List<int> Options { get; set; } = new List<int>();
        public bool CanPass { get; set; }
    }

    public sealed class PendingActionSnapshot
    {
        public PendingActionType Type { get; set; }
        public int A { get; set; }
        public int B { get; set; }
    }
}
