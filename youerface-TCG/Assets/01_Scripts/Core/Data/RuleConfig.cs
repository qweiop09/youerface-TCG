namespace TCG.Core.Data
{
    /// <summary>
    /// 규칙 수치. 규칙 코드에 숫자를 박지 않고 전부 여기서 읽는다.
    /// 기본값은 GameDesign §3.2의 임시값(하스스톤 기준)이며 영역 칸 수만 확정값이다.
    /// </summary>
    public sealed class RuleConfig
    {
        // --- 영웅 / 코스트 (미정, 임시값) ---
        public int HeroHealth { get; set; } = 30;
        /// <summary>첫 턴의 최대 코스트.</summary>
        public int StartingMaxCost { get; set; } = 1;
        /// <summary>턴마다 늘어나는 최대 코스트.</summary>
        public int MaxCostGainPerTurn { get; set; } = 1;
        /// <summary>최대 코스트 상한.</summary>
        public int MaxCostLimit { get; set; } = 10;

        // --- 덱 / 손패 (미정, 임시값) ---
        public int DeckSize { get; set; } = 30;
        public int MaxCopiesPerCard { get; set; } = 2;
        /// <summary>손패 최대. 초과 드로우 처리는 규칙 엔진 담당.</summary>
        public int HandLimit { get; set; } = 10;
        public int FirstPlayerStartingHand { get; set; } = 3;
        public int SecondPlayerStartingHand { get; set; } = 4;
        /// <summary>후공에게 보정 카드를 주는가.</summary>
        public bool SecondPlayerGetsBonusCard { get; set; } = true;
        /// <summary>덱 소진 시 피로 피해를 주는가.</summary>
        public bool FatigueEnabled { get; set; } = true;
        public int TurnTimeLimitSeconds { get; set; } = 75;

        // --- 영역 칸 수 (확정, GameDesign §4.1) ---
        public int MinionSlots { get; set; } = 3;
        public int FieldSlots { get; set; } = 1;
        public int CompanionSlots { get; set; } = 4;
        public int SummonSlots { get; set; } = 4;

        public static RuleConfig CreateDefault() => new RuleConfig();
    }
}
