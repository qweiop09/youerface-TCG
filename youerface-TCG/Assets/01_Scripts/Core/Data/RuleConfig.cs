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
        /// <summary>선공의 첫 턴 최대 코스트.</summary>
        public int StartingMaxCost { get; set; } = 1;
        /// <summary>
        /// 후공의 시작 최대 코스트 (후공 보정, GameDesign §11.3 확정 Q14). 대전 시작부터 이 값이고 후공 첫 턴에도 늘지 않으며,
        /// 그다음 턴부터 MaxCostGainPerTurn씩 는다.
        /// </summary>
        public int SecondPlayerStartingMaxCost { get; set; } = 2;
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
        /// <summary>후공 시작 손패. 선공보다 1장 더 (후공 보정, GameDesign §11.3 확정 Q14).</summary>
        public int SecondPlayerStartingHand { get; set; } = 4;
        /// <summary>후공에게 보정 카드를 주는가. 보정 카드 없음으로 확정(Q14)되어 기본은 끔.</summary>
        public bool SecondPlayerGetsBonusCard { get; set; } = false;
        /// <summary>덱 소진 시 피로 피해를 주는가.</summary>
        public bool FatigueEnabled { get; set; } = true;
        public int TurnTimeLimitSeconds { get; set; } = 75;

        // --- 온라인 시간 제한·연결 끊김 (GameDesign §8.1: 형태 확정, 숫자 미정·임시값) ---
        /// <summary>체인 기회(선택) 제한 시간. 넘으면 자동 선택.</summary>
        public int DecisionTimeLimitSeconds { get; set; } = 20;
        /// <summary>시간 초과 자동 처리가 연속 이만큼이면 패배. 직접 행동하면 0으로.</summary>
        public int MaxConsecutiveTimeouts { get; set; } = 3;
        /// <summary>끊긴 플레이어의 차례에서 자동 선택 전에 기다리는 시간.</summary>
        public int DisconnectedActionWaitSeconds { get; set; } = 20;
        /// <summary>끊긴 채 자동 선택이 이만큼이면 패배 (시간 초과 횟수와 따로 셈).</summary>
        public int MaxDisconnectedAutoActions { get; set; } = 3;
        /// <summary>게스트가 방장 재접속을 기다리는 시간. 넘으면 대전 종료(승패 미정 Q13, 지금은 무효).</summary>
        public int HostReconnectWaitSeconds { get; set; } = 60;

        // --- 전투 (하스스톤 기준 임시값) ---
        /// <summary>기물 1개가 한 턴에 공격할 수 있는 횟수.</summary>
        public int AttacksPerTurn { get; set; } = 1;
        /// <summary>낸 턴에 바로 공격할 수 있는가 (하스스톤: 불가).</summary>
        public bool CanAttackOnPlayedTurn { get; set; } = false;

        // --- 엔진 안전장치 ---
        /// <summary>명령 1개에서 처리할 수 있는 효과 발동 수. 넘으면 남은 발동을 버린다(무한 루프 방지).</summary>
        public int MaxTriggerResolutionsPerAction { get; set; } = 1000;

        // --- 영역 칸 수 (확정, GameDesign §4.1) ---
        public int MinionSlots { get; set; } = 5;
        public int FieldSlots { get; set; } = 1;
        public int CompanionSlots { get; set; } = 4;
        public int SummonSlots { get; set; } = 4;

        // --- 카드 데이터 (개발 측 제안값, GameDesign §11.1) ---
        /// <summary>카드 설명(flavor) 최대 글자 수 (string 길이, 줄바꿈 포함). 제안값.</summary>
        public int MaxFlavorLength { get; set; } = 100;

        public static RuleConfig CreateDefault() => new RuleConfig();
    }
}
