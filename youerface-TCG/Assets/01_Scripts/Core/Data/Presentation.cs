namespace TCG.Core.Data
{
    // 연출 설정 (GameDesign §7.3). 규칙에는 영향이 없고 Client의 연출 재생기만 읽는다.
    // 연출 종류는 문자열 ID라서, 새 연출을 추가해도 데이터 형식은 바뀌지 않는다.

    /// <summary>카드의 시스템 연출 설정. 등장·퇴장 연출은 끌 수 없고 종류만 고른다.</summary>
    public sealed class CardPresentation
    {
        public const string Default = "default";

        /// <summary>등장(소환·발동) 연출 종류.</summary>
        public string Enter { get; set; } = Default;
        /// <summary>퇴장(사망·파괴·교체) 연출 종류.</summary>
        public string Exit { get; set; } = Default;

        /// <summary>원거리 공격 연출: 제자리에서 대상에 이펙트만 띄운다.</summary>
        public const string AttackRanged = "ranged";

        /// <summary>
        /// 공격(전투 피해를 줄 때) 연출 종류 (기물, 스키마 v5). <see cref="Default"/> = 돌진(날아가서 부딪힘),
        /// <see cref="AttackRanged"/> = 원거리(이펙트만). 반격도 같은 연출을 쓴다.
        /// </summary>
        public string Attack { get; set; } = Default;
    }

    /// <summary>여러 대상에게 피해를 줄 때 피격 연출 방식.</summary>
    public enum HitPresentationMode
    {
        /// <summary>한 번에 모두.</summary>
        Simultaneous,
        /// <summary>하나씩 차례로.</summary>
        Sequential,
    }

    /// <summary>효과 연출 설정. 발동 연출(체인에 올라갈 때)과 처리 연출(처리될 때). 끌 수 있다.</summary>
    public sealed class EffectPresentation
    {
        /// <summary>false면 이 효과의 발동·처리 연출을 재생하지 않는다 (드로우·피해 같은 시스템 연출은 그대로).</summary>
        public bool Enabled { get; set; } = true;
        public string Activation { get; set; } = CardPresentation.Default;
        public string Resolution { get; set; } = CardPresentation.Default;
        public HitPresentationMode HitMode { get; set; } = HitPresentationMode.Simultaneous;
    }
}
