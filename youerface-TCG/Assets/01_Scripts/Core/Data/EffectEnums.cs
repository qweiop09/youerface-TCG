namespace TCG.Core.Data
{
    // 효과 블록 종류. 전부 GameDesign §7의 초기 후보이며 최종 목록은 미정(Q8).
    // JSON에는 이름 문자열로 저장되므로 항목 순서를 바꿔도 카드 데이터는 깨지지 않는다.

    /// <summary>효과가 발동하는 시점.</summary>
    public enum TriggerType
    {
        /// <summary>등장(소환·발동) 시</summary>
        OnEnter,
        /// <summary>퇴장(사망·파괴·교체) 시</summary>
        OnExit,
        /// <summary>내 턴 시작 시</summary>
        OnTurnStart,
        /// <summary>내 턴 종료 시 (소환물 자동 발동)</summary>
        OnTurnEnd,
        /// <summary>효과 피해를 받을 때 (GameDesign §7.6)</summary>
        OnEffectDamageTaken,
        /// <summary>전투 피해(공격·반격)를 받을 때</summary>
        OnCombatDamageTaken,
        /// <summary>모든 피해를 받을 때 (효과·전투·피로). 스키마 v2의 "피격 시"(OnDamaged)가 이리로 옮겨진다.</summary>
        OnAnyDamageTaken,
        /// <summary>효과 피해를 줄 때</summary>
        OnEffectDamageDealt,
        /// <summary>전투 피해(공격·반격)를 줄 때</summary>
        OnCombatDamageDealt,
        /// <summary>모든 피해를 줄 때 (효과·전투. 피로 피해는 주는 쪽이 없음)</summary>
        OnAnyDamageDealt,
        /// <summary>공격 시</summary>
        OnAttack,
        /// <summary>카드 사용 시</summary>
        OnCardPlayed,
        /// <summary>상시(지속)</summary>
        Aura,
    }

    /// <summary>피해 유형 (GameDesign §7.6).</summary>
    public enum DamageType
    {
        /// <summary>카드 효과의 "피해" 행동</summary>
        Effect,
        /// <summary>공격과 반격</summary>
        Combat,
        /// <summary>덱 소진 규칙 피해. "모든 피해를 받을 때"에만 걸린다.</summary>
        Fatigue,
    }

    /// <summary>효과 발동 조건.</summary>
    public enum ConditionType
    {
        /// <summary>체력이 Value 이하</summary>
        HealthAtMost,
        /// <summary>내 기물이 Value개 이상</summary>
        FriendlyMinionCountAtLeast,
        /// <summary>손패가 Value장 이하</summary>
        HandCountAtMost,
        /// <summary>CardType 종류 카드가 존재</summary>
        CardTypeExists,
    }

    /// <summary>효과 대상.</summary>
    public enum TargetType
    {
        Self,
        FriendlyHero,
        EnemyHero,
        /// <summary>무작위 적 기물 Count개</summary>
        RandomEnemyMinion,
        AllEnemyMinions,
        AllFriendlyMinions,
        /// <summary>플레이어가 선택한 대상</summary>
        Chosen,
    }

    /// <summary>효과 행동.</summary>
    public enum ActionType
    {
        Damage,
        Heal,
        Draw,
        Discard,
        /// <summary>공격력 증감 (Amount 음수 = 감소)</summary>
        ModifyAttack,
        /// <summary>체력 증감 (Amount 음수 = 감소)</summary>
        ModifyHealth,
        /// <summary>파괴 (필드·동료·소환물은 체력이 없으므로 효과로만 제거됨, GameDesign §5.1)</summary>
        Destroy,
        /// <summary>소환물 생성. 소환물 정의를 Summon에 포함한다.</summary>
        CreateSummon,
        /// <summary>사용 횟수 증감</summary>
        ModifyUsage,
        /// <summary>코스트 증감</summary>
        ModifyCost,
    }
}
