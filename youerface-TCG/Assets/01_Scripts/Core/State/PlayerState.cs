using System;
using System.Collections.Generic;
using TCG.Core.Data;

namespace TCG.Core.State
{
    /// <summary>플레이어 1명의 상태. 각 플레이어가 자기 영역을 따로 가진다(GameDesign §4.1).</summary>
    public sealed class PlayerState
    {
        public int Index { get; }
        public HeroState Hero { get; }

        /// <summary>이번 턴의 최대 코스트.</summary>
        public int MaxCost { get; set; }
        /// <summary>이번 턴에 남은 코스트.</summary>
        public int AvailableCost { get; set; }
        /// <summary>덱 소진 후 드로우 시도 횟수 (피로 피해 계산용).</summary>
        public int FatigueCount { get; set; }
        /// <summary>지금까지 시작한 자기 턴 수.</summary>
        public int TurnsTaken { get; set; }

        public Zone Deck { get; }
        public Zone Hand { get; }
        public Zone Graveyard { get; }
        public Zone Minions { get; }
        public Zone Field { get; }
        public Zone Companions { get; }
        public Zone Summons { get; }
        public Zone Exile { get; }

        /// <summary>모든 영역. 순회 순서는 <see cref="ZoneType"/> 선언 순서로 고정.</summary>
        public IReadOnlyList<Zone> Zones { get; }
        /// <summary>보드 영역(기물·필드·동료·소환물). 효과 발동 순서도 이 순서를 따른다.</summary>
        public IReadOnlyList<Zone> BoardZones { get; }

        public PlayerState(int index, int heroId, RuleConfig rules)
        {
            if (rules == null)
                throw new ArgumentNullException(nameof(rules));

            Index = index;
            Hero = new HeroState(heroId, index, rules.HeroHealth);

            Deck = new Zone(ZoneType.Deck, Zone.Unlimited);
            Hand = new Zone(ZoneType.Hand, rules.HandLimit);
            Graveyard = new Zone(ZoneType.Graveyard, Zone.Unlimited);
            Minions = new Zone(ZoneType.Minion, rules.MinionSlots, usesSlots: true);
            Field = new Zone(ZoneType.Field, rules.FieldSlots);
            Companions = new Zone(ZoneType.Companion, rules.CompanionSlots);
            Summons = new Zone(ZoneType.Summon, rules.SummonSlots);
            Exile = new Zone(ZoneType.Exile, Zone.Unlimited);

            Zones = new[] { Deck, Hand, Graveyard, Minions, Field, Companions, Summons, Exile };
            BoardZones = new[] { Minions, Field, Companions, Summons };
        }

        public Zone GetZone(ZoneType type)
        {
            switch (type)
            {
                case ZoneType.Deck: return Deck;
                case ZoneType.Hand: return Hand;
                case ZoneType.Graveyard: return Graveyard;
                case ZoneType.Minion: return Minions;
                case ZoneType.Field: return Field;
                case ZoneType.Companion: return Companions;
                case ZoneType.Summon: return Summons;
                case ZoneType.Exile: return Exile;
                default: throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }
        }

        public Entity FindEntity(int entityId)
        {
            for (int i = 0; i < Zones.Count; i++)
            {
                var found = Zones[i].FindById(entityId);
                if (found != null)
                    return found;
            }
            return null;
        }
    }
}
