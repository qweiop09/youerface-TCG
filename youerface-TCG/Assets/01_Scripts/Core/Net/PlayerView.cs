using System.Collections.Generic;
using TCG.Core.Data;
using TCG.Core.Events;
using TCG.Core.Random;
using TCG.Core.State;

namespace TCG.Core.Net
{
    /// <summary>가려진 카드 자리에 쓰는 정의. 화면은 이 정의를 보면 뒷면으로 그린다.</summary>
    public static class HiddenCard
    {
        public const string Id = "__hidden__";

        public static readonly CardDefinition Definition = new CardDefinition
        {
            Id = Id,
            Name = "",
            Type = CardType.Spell,
        };

        public static bool Is(Entity entity) => entity != null && ReferenceEquals(entity.Definition, Definition);
    }

    /// <summary>
    /// 플레이어 시야 (Architecture §6): 양쪽 덱 내용·순서와 상대 손패를 가린 상태.
    /// 호스트는 진짜 상태에서 시야 상태를 만들어 각 플레이어에게 보낸다. 난수 상태도 지운다.
    /// </summary>
    public static class PlayerView
    {
        /// <summary>viewer에게 가려지는 위치인가: 덱(내 덱 포함), 상대 손패.</summary>
        public static bool IsHiddenFrom(Entity entity, int viewer)
        {
            if (entity?.Location == null)
                return false;
            var zone = entity.Location.Value;
            return zone == ZoneType.Deck || (zone == ZoneType.Hand && entity.OwnerIndex != viewer);
        }

        public static GameState Create(GameState state, int viewer)
        {
            var view = state.Clone();
            view.Rng.RestoreState(new DeterministicRng(0).SaveState());
            for (int p = 0; p < GameState.PlayerCount; p++)
            {
                var zones = view.GetPlayer(p).Zones;
                for (int z = 0; z < zones.Count; z++)
                {
                    var entities = zones[z].Entities;
                    for (int i = 0; i < entities.Count; i++)
                    {
                        if (IsHiddenFrom(entities[i], viewer))
                            view.ReplaceEntity(Hide(entities[i]));
                    }
                }
            }
            return view;
        }

        static Entity Hide(Entity entity) => new Entity(entity.Id, entity.OwnerIndex, HiddenCard.Definition);

        /// <summary>
        /// before에서 가려졌지만 after에서 보이는 카드(낸 카드, 뽑은 카드 등)를 공개한 before 복사본.
        /// 연출이 "손패에서 나오는 카드"를 앞면으로 그릴 수 있게 한다.
        /// </summary>
        public static GameState Reveal(GameState before, GameState after)
        {
            var copy = before.Clone();
            for (int id = 1; id < copy.NextEntityId; id++)
            {
                var mine = copy.GetEntity(id);
                if (mine == null || !HiddenCard.Is(mine))
                    continue;
                var seen = after.GetEntity(id);
                if (seen != null && !HiddenCard.Is(seen))
                    copy.ReplaceEntity(seen.Clone());
            }
            return copy;
        }

        /// <summary>가려진 카드의 수치 변화(손패 코스트 감소 등)는 빼고 넘긴다.</summary>
        public static List<GameEvent> FilterEvents(IReadOnlyList<GameEvent> events, GameState viewAfter)
        {
            var result = new List<GameEvent>(events.Count);
            for (int i = 0; i < events.Count; i++)
            {
                var e = events[i];
                int? subject = e is StatsChangedEvent stats ? stats.EntityId
                    : e is UsageChangedEvent usage ? usage.EntityId
                    : (int?)null;
                if (subject.HasValue && HiddenCard.Is(viewAfter.GetEntity(subject.Value)))
                    continue;
                result.Add(e);
            }
            return result;
        }
    }
}
