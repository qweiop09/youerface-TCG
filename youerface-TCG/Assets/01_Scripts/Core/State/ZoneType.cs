namespace TCG.Core.State
{
    /// <summary>플레이어별 영역 (GameDesign §4.1).</summary>
    public enum ZoneType
    {
        Deck,
        Hand,
        Graveyard,
        Minion,
        Field,
        Companion,
        Summon,
        /// <summary>소멸: 이번 대전에서 완전히 제거된 카드 (일회성 주문, 손패 초과 드로우).</summary>
        Exile,
    }

    public static class ZoneTypeExtensions
    {
        /// <summary>보드(전장) 영역인가: 기물·필드·동료·소환물.</summary>
        public static bool IsBoard(this ZoneType type)
        {
            return type == ZoneType.Minion || type == ZoneType.Field
                || type == ZoneType.Companion || type == ZoneType.Summon;
        }
    }
}
