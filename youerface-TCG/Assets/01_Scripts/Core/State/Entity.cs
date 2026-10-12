using System;
using System.Collections.Generic;
using TCG.Core.Data;

namespace TCG.Core.State
{
    /// <summary>
    /// 대전 중의 카드 1장(덱·손패·묘지·각 영역 어디에 있든). 원본 정의에서 복사한 값을 가진다.
    /// 공격력·체력·사용 횟수는 정의와 같이 선택적 값이다.
    /// 수치는 "기본값(영구 변경 포함) + 상시 효과 보정"으로 나눠 저장하고, 체력은 받은 피해량으로 관리한다.
    /// </summary>
    public sealed class Entity
    {
        /// <summary>대전 중 순차 발급되는 ID (<see cref="GameState.AllocateEntityId"/>).</summary>
        public int Id { get; }
        public int OwnerIndex { get; }
        public CardDefinition Definition { get; }
        public CardType Type => Definition.Type;

        /// <summary>현재 있는 영역. 어느 영역에도 없으면 null. <see cref="Zone"/>이 관리한다.</summary>
        public ZoneType? Location { get; internal set; }
        public bool IsOnBoard => Location.HasValue && Location.Value.IsBoard();

        // --- 코스트 ---
        public int BaseCost { get; set; }
        public int AuraCost { get; set; }
        public int Cost => Math.Max(0, BaseCost + AuraCost);

        // --- 공격력 (기물 전용) ---
        public int? BaseAttack { get; set; }
        public int AuraAttack { get; set; }
        public int? Attack => BaseAttack.HasValue ? Math.Max(0, BaseAttack.Value + AuraAttack) : (int?)null;

        // --- 체력 (기물 전용) ---
        public int? BaseMaxHealth { get; set; }
        public int AuraHealth { get; set; }
        /// <summary>받은 피해량. 회복하면 줄어든다.</summary>
        public int Damage { get; set; }
        public int? MaxHealth => BaseMaxHealth.HasValue ? BaseMaxHealth.Value + AuraHealth : (int?)null;
        public int? Health => MaxHealth.HasValue ? MaxHealth.Value - Damage : (int?)null;

        /// <summary>사용 횟수. 소환물·동료(선택)만 가진다.</summary>
        public int? Usage { get; set; }
        public List<Keyword> Keywords { get; }

        // --- 전투 상태 ---
        /// <summary>이번 턴에 낸 기물인가 (공격 제한용).</summary>
        public bool PlayedThisTurn { get; set; }
        public int AttacksThisTurn { get; set; }

        /// <summary>"같은 소환물" 판정용 키. 같은 카드의 같은 효과가 만든 소환물은 같은 키를 가진다.</summary>
        public string SummonKey { get; set; }

        public Entity(int id, int ownerIndex, CardDefinition definition)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            Id = id;
            OwnerIndex = ownerIndex;
            BaseCost = definition.Cost;
            BaseAttack = definition.Attack;
            BaseMaxHealth = definition.Health;
            Usage = definition.Usage;
            Keywords = definition.Keywords != null ? new List<Keyword>(definition.Keywords) : new List<Keyword>();
        }

        public bool HasKeyword(Keyword keyword) => Keywords.Contains(keyword);

        public bool IsDead => Health.HasValue && Health.Value <= 0;
    }
}
