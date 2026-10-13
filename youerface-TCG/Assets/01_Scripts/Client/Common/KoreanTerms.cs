using TCG.Core.AI;
using TCG.Core.Data;

namespace TCG.Client.Common
{
    /// <summary>화면에 표시하는 용어 (GameDesign §2 용어 기준).</summary>
    public static class KoreanTerms
    {
        public static string Of(CardType type)
        {
            switch (type)
            {
                case CardType.Minion: return "기물";
                case CardType.Spell: return "주문";
                case CardType.Field: return "필드";
                case CardType.Companion: return "동료";
                case CardType.Summon: return "소환물";
                default: return type.ToString();
            }
        }

        public static string Of(AiDifficulty difficulty)
        {
            switch (difficulty)
            {
                case AiDifficulty.Easy: return "쉬움";
                case AiDifficulty.Normal: return "보통";
                case AiDifficulty.Hard: return "어려움";
                case AiDifficulty.Challenge: return "챌린지";
                default: return difficulty.ToString();
            }
        }

        public static string Of(Keyword keyword)
        {
            switch (keyword)
            {
                case Keyword.Taunt: return "도발";
                case Keyword.Stealth: return "은신";
                case Keyword.Vanguard: return "선봉";
                case Keyword.Exhaust: return "일회성";
                case Keyword.TreasureChest: return "보물상자";
                default: return keyword.ToString();
            }
        }

        /// <summary>키워드 규칙 설명 (GameDesign §7.1).</summary>
        public static string Describe(Keyword keyword)
        {
            switch (keyword)
            {
                case Keyword.Taunt: return "상대는 도발 기물부터 공격해야 한다.";
                case Keyword.Stealth: return "기물의 공격 대상이 될 수 없다. 공격해도 풀리지 않는다.";
                case Keyword.Vanguard: return "등장하면 카드 1장을 뽑는다.";
                case Keyword.Exhaust: return "사용하면 이번 대전에서 제거된다.";
                case Keyword.TreasureChest: return "쓰러지면 카드 1장을 뽑는다.";
                default: return "";
            }
        }

        public static string Of(TargetType type)
        {
            switch (type)
            {
                case TargetType.Self: return "자신";
                case TargetType.FriendlyHero: return "내 영웅";
                case TargetType.EnemyHero: return "상대 영웅";
                case TargetType.RandomEnemyMinion: return "무작위 적 기물";
                case TargetType.AllEnemyMinions: return "모든 적 기물";
                case TargetType.AllFriendlyMinions: return "모든 아군 기물";
                case TargetType.Chosen: return "선택 대상";
                default: return type.ToString();
            }
        }

        public static string Of(ActionType type)
        {
            switch (type)
            {
                case ActionType.Damage: return "피해";
                case ActionType.Heal: return "회복";
                case ActionType.Draw: return "드로우";
                case ActionType.Discard: return "버리기";
                case ActionType.ModifyAttack: return "공격력 증감";
                case ActionType.ModifyHealth: return "체력 증감";
                case ActionType.Destroy: return "파괴";
                case ActionType.CreateSummon: return "소환물 생성";
                case ActionType.ModifyUsage: return "사용 횟수 증감";
                case ActionType.ModifyCost: return "코스트 증감";
                default: return type.ToString();
            }
        }

        public static string Of(ConditionType type)
        {
            switch (type)
            {
                case ConditionType.HealthAtMost: return "체력 n 이하";
                case ConditionType.FriendlyMinionCountAtLeast: return "내 기물 n개 이상";
                case ConditionType.HandCountAtMost: return "손패 n장 이하";
                case ConditionType.CardTypeExists: return "특정 종류 카드 존재";
                default: return type.ToString();
            }
        }

        public static string Of(SummonDuplicateMode mode)
        {
            switch (mode)
            {
                case SummonDuplicateMode.AddNew: return "하나 더 놓기";
                case SummonDuplicateMode.RefreshUsage: return "사용 횟수 갱신";
                default: return mode.ToString();
            }
        }

        public static string Of(EffectActivation activation) => activation == EffectActivation.Optional ? "선택 발동" : "강제 발동";

        public static string Of(TCG.Core.Scoring.ScoreLineKind kind)
        {
            switch (kind)
            {
                case TCG.Core.Scoring.ScoreLineKind.Base: return "기본";
                case TCG.Core.Scoring.ScoreLineKind.Attack: return "공격력";
                case TCG.Core.Scoring.ScoreLineKind.Health: return "체력";
                case TCG.Core.Scoring.ScoreLineKind.Cost: return "코스트";
                case TCG.Core.Scoring.ScoreLineKind.Keyword: return "키워드";
                case TCG.Core.Scoring.ScoreLineKind.Effect: return "효과";
                case TCG.Core.Scoring.ScoreLineKind.PenaltyCap: return "패널티 상한";
                default: return kind.ToString();
            }
        }
    }
}
