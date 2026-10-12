namespace TCG.Core.Data
{
    /// <summary>
    /// 키워드 (GameDesign §7.1). 동작과 붙일 수 있는 카드 종류는 <see cref="Effects.EffectRegistry"/>에 등록한다.
    /// 새 키워드는 여기에 항목을 추가하고 레지스트리에 등록한다. JSON에는 이름 문자열로 저장된다.
    /// </summary>
    public enum Keyword
    {
        /// <summary>도발: 상대는 도발 기물부터 공격해야 한다.</summary>
        Taunt,
        /// <summary>은신: 기물의 공격 대상이 될 수 없다(효과 대상은 가능). 공격해도 풀리지 않는다.</summary>
        Stealth,
        /// <summary>선봉: 등장하면 주인이 1장 드로우.</summary>
        Vanguard,
        /// <summary>일회성(주문 전용): 사용하면 묘지 대신 소멸 영역으로 가서 이번 대전에서 제거된다.</summary>
        Exhaust,
        /// <summary>보물상자: 쓰러지면(사망·파괴) 주인이 1장 드로우.</summary>
        TreasureChest,
    }
}
