namespace TCG.Core.Scoring
{
    /// <summary>
    /// 점수 가중치 테이블. 대전 시 양쪽 버전이 같아야 한다.
    /// 현재 값은 전부 임시이며 세부 공식은 사용자와 논의 후 확정한다(GameDesign §6, Q7).
    /// </summary>
    public sealed class ScoreTable
    {
        public int Version { get; set; }

        public int AttackWeight { get; set; } = 1;
        public int HealthWeight { get; set; } = 1;
        public int UsageWeight { get; set; } = 1;
        public int KeywordWeight { get; set; } = 1;
        /// <summary>효과 1개당 점수. 효과별 가치 구분은 Q7 논의 후.</summary>
        public int EffectWeight { get; set; } = 1;
        /// <summary>코스트 1당 감소하는 점수.</summary>
        public int CostWeight { get; set; } = 1;

        /// <summary>최소 점수. null이면 제한 없음(음수 허용 여부 미정, Q7).</summary>
        public int? MinScore { get; set; }

        public static ScoreTable CreateTemporary() => new ScoreTable();
    }
}
