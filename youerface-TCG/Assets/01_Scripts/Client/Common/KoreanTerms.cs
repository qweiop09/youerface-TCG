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
    }
}
