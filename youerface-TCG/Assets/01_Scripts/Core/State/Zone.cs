using System;
using System.Collections.Generic;

namespace TCG.Core.State
{
    /// <summary>
    /// 순서가 있는 카드 칸 묶음. 칸 수를 넘으면 추가가 실패한다.
    /// 가득 찼을 때의 처리(낼 수 없음, 소멸, 필드 교체 등)는 규칙 엔진이 정한다(GameDesign Q2).
    /// </summary>
    public sealed class Zone
    {
        public const int Unlimited = -1;

        readonly List<Entity> _entities = new List<Entity>();

        public ZoneType Type { get; }
        /// <summary>최대 칸 수. <see cref="Unlimited"/>면 제한 없음.</summary>
        public int Capacity { get; }

        public IReadOnlyList<Entity> Entities => _entities;
        public int Count => _entities.Count;
        public bool IsUnlimited => Capacity == Unlimited;
        public bool IsFull => !IsUnlimited && _entities.Count >= Capacity;
        public int FreeSlots => IsUnlimited ? int.MaxValue : Capacity - _entities.Count;

        public Zone(ZoneType type, int capacity)
        {
            if (capacity < 0 && capacity != Unlimited)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            Type = type;
            Capacity = capacity;
        }

        /// <summary>맨 끝에 추가. 가득 찼으면 false.</summary>
        public bool TryAdd(Entity entity) => TryInsert(entity, _entities.Count);

        /// <summary>지정 위치에 추가. 가득 찼으면 false.</summary>
        public bool TryInsert(Entity entity, int index)
        {
            if (entity == null)
                throw new ArgumentNullException(nameof(entity));
            if (index < 0 || index > _entities.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            if (_entities.Contains(entity))
                throw new InvalidOperationException($"Entity {entity.Id} is already in {Type} zone.");
            if (IsFull)
                return false;

            _entities.Insert(index, entity);
            return true;
        }

        public bool Remove(Entity entity) => _entities.Remove(entity);

        public bool Contains(Entity entity) => _entities.Contains(entity);

        public int IndexOf(Entity entity) => _entities.IndexOf(entity);

        public Entity FindById(int entityId)
        {
            for (int i = 0; i < _entities.Count; i++)
            {
                if (_entities[i].Id == entityId)
                    return _entities[i];
            }
            return null;
        }
    }
}
