using System.Collections.Generic;
using TCG.Core.Data;

namespace TCG.Core.Scoring
{
    /// <summary>
    /// 점수 가중치 테이블 (GameDesign §6.1). 대전 시 양쪽 버전이 같아야 한다.
    /// 배율은 백분율 정수(100 = ×1). 표에 없는 블록은 행동 0점, 배율 100으로 읽는다.
    /// 새 블록·키워드를 추가하면 여기에 값을 추가하고 Version을 올린다.
    /// </summary>
    public sealed class ScoreTable
    {
        /// <summary>2: 피해 트리거 6종 추가(피격 시 대체, GameDesign §7.6).</summary>
        public int Version { get; set; } = 2;

        /// <summary>카드 기본 점수. 공격력+체력 = 2×코스트+1인 카드가 0점이 되게 하는 값. 포함된 소환물에는 붙지 않는다.</summary>
        public int BaseScore { get; set; } = -10;
        public int AttackWeight { get; set; } = 10;
        public int HealthWeight { get; set; } = 10;
        /// <summary>코스트 1당 감소하는 점수.</summary>
        public int CostWeight { get; set; } = 20;

        public Dictionary<Keyword, int> KeywordScores { get; set; } = new Dictionary<Keyword, int>
        {
            { Keyword.Taunt, 10 },
            { Keyword.Stealth, 15 },
            { Keyword.Vanguard, 30 },
            { Keyword.TreasureChest, 30 },
            { Keyword.Exhaust, -5 },
        };

        /// <summary>행동 기본점(수치 1당, 절댓값). 파괴는 수치와 무관한 고정값. 소환물 생성은 소환물 점수를 쓰므로 표에 없다.</summary>
        public Dictionary<ActionType, int> ActionScores { get; set; } = new Dictionary<ActionType, int>
        {
            { ActionType.Damage, 10 },
            { ActionType.Heal, 5 },
            { ActionType.Draw, 30 },
            { ActionType.Discard, 20 },
            { ActionType.ModifyAttack, 10 },
            { ActionType.ModifyHealth, 10 },
            { ActionType.Destroy, 60 },
            { ActionType.ModifyCost, 15 },
            { ActionType.ModifyUsage, 10 },
        };

        public Dictionary<TargetType, int> TargetMultipliers { get; set; } = new Dictionary<TargetType, int>
        {
            { TargetType.Self, 100 },
            { TargetType.FriendlyHero, 100 },
            { TargetType.EnemyHero, 100 },
            { TargetType.Chosen, 130 },
            { TargetType.AllEnemyMinions, 250 },
            { TargetType.AllFriendlyMinions, 250 },
        };
        /// <summary>무작위 적 기물 n: 1개당 배율.</summary>
        public int RandomTargetPerCount { get; set; } = 100;

        public Dictionary<TriggerType, int> TriggerMultipliers { get; set; } = new Dictionary<TriggerType, int>
        {
            { TriggerType.OnEnter, 100 },
            { TriggerType.OnExit, 80 },
            // 피해 트리거 6종: 우선 옛 "피격 시" 배율(×1.2)을 그대로 쓰고 개별 조정한다 (GameDesign §7.6, 제안).
            { TriggerType.OnEffectDamageTaken, 120 },
            { TriggerType.OnCombatDamageTaken, 120 },
            { TriggerType.OnAnyDamageTaken, 120 },
            { TriggerType.OnEffectDamageDealt, 120 },
            { TriggerType.OnCombatDamageDealt, 120 },
            { TriggerType.OnAnyDamageDealt, 120 },
            { TriggerType.OnAttack, 120 },
            { TriggerType.OnCardPlayed, 150 },
            { TriggerType.Aura, 150 },
            { TriggerType.OnTurnStart, 200 },
            { TriggerType.OnTurnEnd, 200 },
        };

        /// <summary>사용 횟수가 있는 카드에서 반복 트리거 배율을 사용 횟수로 대신하는 트리거.</summary>
        public List<TriggerType> RepeatingTriggers { get; set; } = new List<TriggerType>
        {
            TriggerType.OnTurnStart, TriggerType.OnTurnEnd, TriggerType.OnAttack, TriggerType.OnCardPlayed,
            TriggerType.OnEffectDamageTaken, TriggerType.OnCombatDamageTaken, TriggerType.OnAnyDamageTaken,
            TriggerType.OnEffectDamageDealt, TriggerType.OnCombatDamageDealt, TriggerType.OnAnyDamageDealt,
        };
        /// <summary>사용 횟수 1당 반복 트리거 배율.</summary>
        public int UsageMultiplierPerUse { get; set; } = 100;
        /// <summary>사용 횟수로 정하는 반복 트리거 배율의 상한.</summary>
        public int UsageMultiplierCap { get; set; } = 300;

        /// <summary>조건 1개당 배율.</summary>
        public int ConditionMultiplier { get; set; } = 70;
        /// <summary>선택 발동 효과 배율. 선택 발동 패널티는 0점.</summary>
        public int OptionalMultiplier { get; set; } = 110;

        /// <summary>패널티(음수 효과·음수 키워드)로 깎이는 점수 합계의 상한(양수).</summary>
        public int PenaltyCap { get; set; } = 30;

        /// <summary>카드에 표시할 때의 최소 점수. 카드 편집 화면은 실제 값(음수 포함)을 보여 준다.</summary>
        public int DisplayMinimum { get; set; } = 0;

        public static ScoreTable CreateDefault() => new ScoreTable();

        public int KeywordScore(Keyword keyword) => KeywordScores.TryGetValue(keyword, out int v) ? v : 0;
        public int ActionScore(ActionType action) => ActionScores.TryGetValue(action, out int v) ? v : 0;
        public int TargetMultiplier(TargetType target) => TargetMultipliers.TryGetValue(target, out int v) ? v : 100;
        public int TriggerMultiplier(TriggerType trigger) => TriggerMultipliers.TryGetValue(trigger, out int v) ? v : 100;
    }
}
