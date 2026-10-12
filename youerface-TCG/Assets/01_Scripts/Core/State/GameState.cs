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

        public int AllocateEntityId() => _nextEntityId++;

        /// <summary>새 ID로 카드 Entity를 만든다. 영역에는 넣지 않는다.</summary>
        public Entity CreateEntity(int ownerIndex, CardDefinition definition)
        {
            if (ownerIndex < 0 || ownerIndex >= PlayerCount)
                throw new ArgumentOutOfRangeException(nameof(ownerIndex));
            return new Entity(AllocateEntityId(), ownerIndex, definition);
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

        public Entity FindEntity(int entityId)
        {
            for (int i = 0; i < _players.Length; i++)
            {
                var found = _players[i].FindEntity(entityId);
                if (found != null)
                    return found;
            }
            return null;
        }
    }
}
