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
    }
}
