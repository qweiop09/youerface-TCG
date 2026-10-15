namespace TCG.Core.State
{
    public enum StatKind
    {
        Attack,
        /// <summary>최대 체력</summary>
        Health,
        Cost,
    }

    /// <summary>
    /// 카드 능력치 변화 1건 (출처 카드별로 합친 값). 자세히 보기에서 "무엇 때문에 얼마나 바뀌었는지" 보여 주는 용도이며
    /// 규칙 계산에는 쓰지 않는다 (수치는 <see cref="Entity"/>의 Base/Aura 값이 기준). 그래서 상태 해시에도 넣지 않는다.
    /// </summary>
    public sealed class StatChange
    {
        public StatKind Stat { get; set; }
        /// <summary>변화량 합계 (음수 = 감소).</summary>
        public int Amount { get; set; }
        /// <summary>효과를 가진 카드 ID (0 = 알 수 없음).</summary>
        public int SourceId { get; set; }
        /// <summary>효과를 가진 카드 이름 (출처가 사라져도 보이게 기록 시점에 남김).</summary>
        public string SourceName { get; set; }
        /// <summary>상시 효과 보정이면 true (출처가 사라지면 함께 사라짐), 영구 변경이면 false.</summary>
        public bool Aura { get; set; }

        public StatChange Clone() => (StatChange)MemberwiseClone();
    }
}
