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
        /// <summary>현재 대응 기회에서 연속으로 패스한 수 (2면 체인이 닫힘).</summary>
        public int ResponsePasses { get; set; }
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
