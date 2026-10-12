using System.Collections.Generic;
using TCG.Client.Board;
using TCG.Client.Cards;
using TCG.Core.Data;
using TCG.Core.Effects;
using TCG.Core.State;
using UnityEngine;

namespace TCG.Client.Presentation
{
    /// <summary>
    /// 이벤트 묶음(명령 1개의 결과) 하나를 재생하는 동안의 문맥.
    /// Before = 명령 직전 상태(복사본), After = 명령 직후 상태(복사본). 둘 다 바뀌지 않는다.
    /// </summary>
    public sealed class PresentationContext
    {
        public BoardView Board { get; }
        public GameState Before { get; }
        public GameState After { get; }
        public EffectRegistry Effects { get; }
        public MonoBehaviour Host { get; }
        /// <summary>주문처럼 영역 없이 잠깐 떠 있는 카드. 체인이 끝나거나 묶음이 끝나면 치운다.</summary>
        public List<CardView> Lingering { get; } = new List<CardView>();

        public PresentationContext(BoardView board, GameState before, GameState after, EffectRegistry effects, MonoBehaviour host)
        {
            Board = board;
            Before = before;
            After = after;
            Effects = effects;
            Host = host;
        }

        public int Viewer => Board.Viewer;
        public bool IsMine(int ownerIndex) => ownerIndex == Viewer;

        /// <summary>
        /// 카드 그림을 새로 만들 때 쓸 수치: 명령 직전의 값. 이번 명령에서 새로 생긴 카드(소환물)는 정의 그대로의 값.
        /// 이후 이벤트가 숫자를 한 단계씩 바꾼다.
        /// </summary>
        public Entity Snapshot(int entityId)
        {
            var before = Before.GetEntity(entityId);
            if (before != null)
                return before;
            var after = After.GetEntity(entityId);
            return after == null ? null : new Entity(after.Id, after.OwnerIndex, after.Definition);
        }

        public int OwnerOf(int entityId) => (After.GetEntity(entityId) ?? Before.GetEntity(entityId))?.OwnerIndex ?? -1;

        /// <summary>이벤트가 가리키는 효과 (카드 효과 목록 기준 순번).</summary>
        public EffectDefinition Effect(int sourceId, int effectIndex)
        {
            var entity = After.GetEntity(sourceId) ?? Before.GetEntity(sourceId);
            return EffectLookup.Get(entity, effectIndex, Effects);
        }

        public CardDefinition Definition(int entityId) => (After.GetEntity(entityId) ?? Before.GetEntity(entityId))?.Definition;
    }
}
