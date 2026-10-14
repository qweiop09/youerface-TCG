using System;
using System.Collections.Generic;
using TCG.Core.Data;
using TCG.Core.Random;

namespace TCG.Core.State
{
    public enum GamePhase
    {
        NotStarted,
        InProgress,
        Finished,
    }

    /// <summary>
    /// 대전 1판의 전체 상태(단일 진실 원천). 상태 변경은 규칙 엔진(M2 GameEngine)을 통해서만 한다.
    /// </summary>
    public sealed class GameState
    {
        public const int PlayerCount = 2;

        readonly PlayerState[] _players;
        /// <summary>ID → 카드. 인덱스 = ID - 1. 영웅 ID 자리는 null. 영역을 떠나 사라진 카드(소환물)도 남는다.</summary>
        readonly List<Entity> _entityTable = new List<Entity>();
        int _nextEntityId = 1;

        public RuleConfig Rules { get; }
        public DeterministicRng Rng { get; }
        public IReadOnlyList<PlayerState> Players => _players;

        public GamePhase Phase { get; set; } = GamePhase.NotStarted;
        public int ActivePlayerIndex { get; set; }
        public int TurnNumber { get; set; }
        /// <summary>승자 인덱스. 무승부 또는 진행 중이면 null.</summary>
        public int? WinnerIndex { get; set; }

        /// <summary>다음에 발급할 Entity ID.</summary>
        public int NextEntityId => _nextEntityId;

        // --- 체인 (GameDesign §7.2) ---
        /// <summary>현재 체인. 마지막이 맨 위(먼저 처리).</summary>
        public List<ChainLink> Chain { get; } = new List<ChainLink>();
        /// <summary>체인이 닫혀 처리 중인가.</summary>
        public bool ChainClosed { get; set; }
        /// <summary>체인 기회를 가진 플레이어. 체인을 쌓는 중이 아니면 -1.</summary>
        public int PriorityPlayer { get; set; } = -1;
        /// <summary>연속으로 패스한 수 (2면 체인이 닫힘).</summary>
        public int PriorityPasses { get; set; }
        /// <summary>체인에 올라가기를 기다리는 효과.</summary>
        public List<PendingTrigger> PendingTriggers { get; } = new List<PendingTrigger>();
        /// <summary>플레이어 선택 대기. null이면 일반 행동을 받을 수 있다.</summary>
        public PendingDecision Decision { get; set; }
        /// <summary>체인이 끝난 뒤 이어서 할 행동 (FIFO).</summary>
        public List<PendingAction> PendingActions { get; } = new List<PendingAction>();
        public int NextTriggerId { get; set; } = 1;
        /// <summary>현재 플레이어 행동 1개에서 처리한 체인 수 (무한 연쇄 방지).</summary>
        public int ResolutionCount { get; set; }

        public GameState(RuleConfig rules, int seed)
        {
            Rules = rules ?? throw new ArgumentNullException(nameof(rules));
            Rng = new DeterministicRng(seed);

            _players = new PlayerState[PlayerCount];
            for (int i = 0; i < PlayerCount; i++)
                _players[i] = new PlayerState(i, AllocateEntityId(), rules);
        }

        public PlayerState GetPlayer(int index) => _players[index];

        public PlayerState GetOpponent(int index) => _players[1 - index];

        public PlayerState ActivePlayer => _players[ActivePlayerIndex];

        public int AllocateEntityId()
        {
            _entityTable.Add(null);
            return _nextEntityId++;
        }

        /// <summary>새 ID로 카드 Entity를 만든다. 영역에는 넣지 않는다.</summary>
        public Entity CreateEntity(int ownerIndex, CardDefinition definition)
        {
            if (ownerIndex < 0 || ownerIndex >= PlayerCount)
                throw new ArgumentOutOfRangeException(nameof(ownerIndex));
            var entity = new Entity(AllocateEntityId(), ownerIndex, definition);
            _entityTable[entity.Id - 1] = entity;
            return entity;
        }

        /// <summary>
        /// 완전한 복사본. AI가 수를 미리 계산해 볼 때 쓴다. 카드 정의(CardDefinition)는 바뀌지 않으므로 공유한다.
        /// </summary>
        public GameState Clone()
        {
            var copy = new GameState(Rules, 1);
            copy.Rng.RestoreState(Rng.SaveState());

            copy._entityTable.Clear();
            for (int i = 0; i < _entityTable.Count; i++)
                copy._entityTable.Add(_entityTable[i]?.Clone());
            copy._nextEntityId = _nextEntityId;

            for (int p = 0; p < PlayerCount; p++)
            {
                var from = _players[p];
                var to = copy._players[p];
                to.Hero.Health = from.Hero.Health;
                to.Hero.MaxHealth = from.Hero.MaxHealth;
                to.MaxCost = from.MaxCost;
                to.AvailableCost = from.AvailableCost;
                to.FatigueCount = from.FatigueCount;
                to.TurnsTaken = from.TurnsTaken;

                for (int z = 0; z < from.Zones.Count; z++)
                {
                    var source = from.Zones[z].Entities;
                    var target = to.Zones[z];
                    for (int i = 0; i < source.Count; i++)
                        target.TryAdd(copy.GetEntity(source[i].Id));
                }
            }

            copy.Phase = Phase;
            copy.ActivePlayerIndex = ActivePlayerIndex;
            copy.TurnNumber = TurnNumber;
            copy.WinnerIndex = WinnerIndex;

            for (int i = 0; i < Chain.Count; i++)
                copy.Chain.Add(Chain[i].Clone());
            copy.ChainClosed = ChainClosed;
            copy.PriorityPlayer = PriorityPlayer;
            copy.PriorityPasses = PriorityPasses;
            for (int i = 0; i < PendingTriggers.Count; i++)
                copy.PendingTriggers.Add(PendingTriggers[i].Clone());
            copy.Decision = Decision;
            copy.PendingActions.AddRange(PendingActions);
            copy.NextTriggerId = NextTriggerId;
            copy.ResolutionCount = ResolutionCount;
            return copy;
        }

        // --- 온라인 시야 상태 복원용 (Net.StateSnapshot, Net.PlayerView) ---

        /// <summary>정해진 ID의 카드를 표에 넣는다(영역에는 넣지 않음). 표가 짧으면 빈 칸을 늘린다.</summary>
        internal void PutEntity(Entity entity)
        {
            while (_entityTable.Count < entity.Id)
                _entityTable.Add(null);
            _entityTable[entity.Id - 1] = entity;
            if (_nextEntityId <= entity.Id)
                _nextEntityId = entity.Id + 1;
        }

        internal void SetNextEntityId(int next)
        {
            while (_entityTable.Count < next - 1)
                _entityTable.Add(null);
            _nextEntityId = next;
        }

        /// <summary>같은 ID의 카드를 바꿔 끼운다. 영역에 있으면 같은 자리에.</summary>
        internal void ReplaceEntity(Entity replacement)
        {
            var old = GetEntity(replacement.Id);
            if (old != null && old.Location.HasValue)
                _players[old.OwnerIndex].GetZone(old.Location.Value).Replace(old, replacement);
            PutEntity(replacement);
        }

        /// <summary>ID로 카드를 찾는다. 어느 영역에도 없는 카드(사라진 소환물 등)도 찾는다.</summary>
        public Entity GetEntity(int entityId)
        {
            int index = entityId - 1;
            return index >= 0 && index < _entityTable.Count ? _entityTable[index] : null;
        }

        /// <summary>ID가 영웅이면 그 영웅, 아니면 null.</summary>
        public HeroState FindHero(int id)
        {
            for (int i = 0; i < _players.Length; i++)
            {
                if (_players[i].Hero.Id == id)
                    return _players[i].Hero;
            }
            return null;
        }

        /// <summary>ID로 카드를 찾는다. 어떤 영역(덱·손패·묘지·보드·소멸)에 있는 카드만.</summary>
        public Entity FindEntity(int entityId)
        {
            var e = GetEntity(entityId);
            return e != null && e.Location.HasValue ? e : null;
        }
    }
}
