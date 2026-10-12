namespace TCG.Core.Data
{
    /// <summary>카드 종류 (GameDesign §5).</summary>
    public enum CardType
    {
        /// <summary>하수인 영역. 공격력·체력 보유, 공격/피격 가능.</summary>
        Minion,
        /// <summary>즉시 효과를 내고 묘지로 감.</summary>
        Spell,
        /// <summary>필드 영역(1칸). 새 필드를 내면 기존 필드와 교체.</summary>
        Field,
        /// <summary>동료 영역. 사용 횟수는 카드마다 선택(없으면 영구 지속).</summary>
        Companion,
        /// <summary>소환물 영역. 다른 카드의 효과로만 생성되며 덱·손패에 들어가지 않음.</summary>
        Summon,
    }
}
