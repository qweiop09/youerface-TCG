using System;
using System.Collections.Generic;
using TCG.Core.Data;

namespace TCG.Core.State
{
    /// <summary>
    /// 대전 중의 카드 1장(덱·손패·묘지·각 영역 어디에 있든). 원본 정의에서 복사한 현재 값을 가진다.
    /// 공격력·체력·사용 횟수는 정의와 같이 선택적 값이다.
    /// </summary>
    public sealed class Entity
    {
        /// <summary>대전 중 순차 발급되는 ID (<see cref="GameState.AllocateEntityId"/>).</summary>
        public int Id { get; }
        public int OwnerIndex { get; }
        public CardDefinition Definition { get; }

        public int Cost { get; set; }
        public int? Attack { get; set; }
        public int? Health { get; set; }
        public int? MaxHealth { get; set; }
        public int? Usage { get; set; }
        public List<Keyword> Keywords { get; }

        public CardType Type => Definition.Type;

        public Entity(int id, int ownerIndex, CardDefinition definition)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            Id = id;
            OwnerIndex = ownerIndex;
            Cost = definition.Cost;
            Attack = definition.Attack;
            Health = definition.Health;
            MaxHealth = definition.Health;
            Usage = definition.Usage;
            Keywords = definition.Keywords != null ? new List<Keyword>(definition.Keywords) : new List<Keyword>();
        }
    }
}
