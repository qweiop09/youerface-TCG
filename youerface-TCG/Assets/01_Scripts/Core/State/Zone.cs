using System;
using System.Collections.Generic;
using TCG.Core.Random;

namespace TCG.Core.State
{
    /// <summary>
    /// 순서가 있는 카드 칸 묶음. 칸 수를 넘으면 추가가 실패한다.
    /// 가득 찼을 때의 처리(낼 수 없음, 소멸, 필드 교체 등)는 규칙 엔진이 정한다(GameDesign Q2).
    ///
    /// 칸 번호 영역(<see cref="UsesSlots"/>, 기물 — GameDesign §4.1): 카드마다 칸 번호(<see cref="Entity.Slot"/>)가 있고
    /// 빈 칸이 생겨도 다른 카드가 밀리지 않는다. 목록은 항상 칸 번호 순(왼쪽부터)이다.
    /// 칸을 정하지 않고 넣으면(효과·AI·복제) 카드의 기존 칸이 비어 있으면 그 칸, 아니면 가운데에 가까운 빈 칸(같으면 왼쪽).
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

        /// <summary>칸 번호 영역인가 (기물).</summary>
        public bool UsesSlots { get; }

        public Zone(ZoneType type, int capacity, bool usesSlots = false)
        {
            if (capacity < 0 && capacity != Unlimited)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            if (usesSlots && capacity == Unlimited)
                throw new ArgumentException("Slot zones need a capacity.", nameof(usesSlots));
            Type = type;
            Capacity = capacity;
            UsesSlots = usesSlots;
        }

        /// <summary>맨 끝에 추가 (칸 번호 영역은 기존 칸 → 가운데에 가까운 빈 칸). 가득 찼으면 false.</summary>
        public bool TryAdd(Entity entity)
        {
            if (!UsesSlots)
                return TryInsert(entity, _entities.Count);
            int slot = entity != null && IsSlotFree(entity.Slot) ? entity.Slot : NearestCenterFreeSlot();
            return TryPlaceAt(entity, slot);
        }

        /// <summary>칸 번호 영역: 이 칸이 있고 비어 있는가.</summary>
        public bool IsSlotFree(int slot)
        {
            if (!UsesSlots || slot < 0 || slot >= Capacity)
                return false;
            for (int i = 0; i < _entities.Count; i++)
            {
                if (_entities[i].Slot == slot)
                    return false;
            }
            return true;
        }

        /// <summary>칸 번호 영역: 가운데에 가까운 빈 칸 (거리가 같으면 왼쪽). 없으면 -1.</summary>
        public int NearestCenterFreeSlot()
        {
            int best = -1, bestDistance = int.MaxValue;
            for (int slot = 0; slot < Capacity; slot++)
            {
                if (!IsSlotFree(slot))
                    continue;
                // 2배로 계산해 짝수 칸 수에서도 정수로 비교 (가운데 = Capacity - 1).
                int distance = Math.Abs(slot * 2 - (Capacity - 1));
                if (distance < bestDistance)
                {
                    best = slot;
                    bestDistance = distance;
                }
            }
            return best;
        }

        /// <summary>칸 번호 영역: 지정한 빈 칸에 놓는다. 칸이 없거나 차 있으면 false.</summary>
        public bool TryPlaceAt(Entity entity, int slot)
        {
            if (entity == null)
                throw new ArgumentNullException(nameof(entity));
            if (!UsesSlots)
                throw new InvalidOperationException($"{Type} zone has no slots.");
            if (_entities.Contains(entity))
                throw new InvalidOperationException($"Entity {entity.Id} is already in {Type} zone.");
            if (IsFull || !IsSlotFree(slot))
                return false;

            int index = 0;
            while (index < _entities.Count && _entities[index].Slot < slot)
                index++;
            _entities.Insert(index, entity);
            entity.Slot = slot;
            entity.Location = Type;
            return true;
        }

        /// <summary>지정 위치(목록 순번)에 추가. 가득 찼으면 false. 칸 번호 영역에는 쓰지 않는다(<see cref="TryPlaceAt"/>).</summary>
        public bool TryInsert(Entity entity, int index)
        {
            if (UsesSlots)
                throw new InvalidOperationException($"{Type} zone uses slots; use TryPlaceAt.");
            if (entity == null)
                throw new ArgumentNullException(nameof(entity));
            if (index < 0 || index > _entities.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            if (_entities.Contains(entity))
                throw new InvalidOperationException($"Entity {entity.Id} is already in {Type} zone.");
            if (IsFull)
                return false;

            _entities.Insert(index, entity);
            entity.Location = Type;
            return true;
        }

        /// <summary>같은 자리에 다른 카드 객체를 끼운다 (시야 상태에서 가려진 카드를 공개할 때).</summary>
        internal void Replace(Entity old, Entity replacement)
        {
            int index = _entities.IndexOf(old);
            if (index < 0)
                return;
            _entities[index] = replacement;
            old.Location = null;
            replacement.Location = Type;
            if (UsesSlots)
                replacement.Slot = old.Slot;
        }

        public bool Remove(Entity entity)
        {
            if (!_entities.Remove(entity))
                return false;
            entity.Location = null;
            if (UsesSlots)
                entity.Slot = -1;
            return true;
        }

        /// <summary>결정론적 셔플 (덱 섞기).</summary>
        public void Shuffle(DeterministicRng rng)
        {
            if (rng == null)
                throw new ArgumentNullException(nameof(rng));
            rng.Shuffle(_entities);
        }

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
