using TCG.Core.Data;
using UnityEngine;

namespace TCG.Client.Cards
{
    /// <summary>임시 카드 그림의 색. 카드 그림(유저 이미지)이 붙기 전까지 종류를 색으로 구분한다.</summary>
    public static class CardPalette
    {
        public static readonly Color Back = new Color(0.16f, 0.18f, 0.28f);
        public static readonly Color BackTrim = new Color(0.78f, 0.64f, 0.32f);
        public static readonly Color Banner = new Color(0.1f, 0.1f, 0.12f);
        public static readonly Color Text = new Color(0.97f, 0.95f, 0.9f);
        public static readonly Color CostBadge = new Color(0.2f, 0.45f, 0.9f);
        public static readonly Color AttackBadge = new Color(0.92f, 0.72f, 0.18f);
        public static readonly Color HealthBadge = new Color(0.8f, 0.2f, 0.2f);
        public static readonly Color UsageBadge = new Color(0.45f, 0.47f, 0.5f);
        // 숫자 색: 원판 색마다 잘 보이는 색을 쓴다.
        /// <summary>파란·빨간·회색 원 위의 기본 숫자.</summary>
        public static readonly Color NumberLight = Color.white;
        /// <summary>노란(공격력) 원 위의 기본 숫자.</summary>
        public static readonly Color NumberDark = new Color(0.12f, 0.1f, 0.08f);
        /// <summary>피해를 입었거나 코스트가 오름 (빨간·파란 원 위).</summary>
        public static readonly Color Worse = new Color(1f, 0.78f, 0.25f);
        /// <summary>강화됨 (빨간·파란 원 위).</summary>
        public static readonly Color BetterLight = new Color(0.62f, 1f, 0.55f);
        /// <summary>강화됨 (노란 원 위).</summary>
        public static readonly Color BetterDark = new Color(0f, 0.42f, 0.08f);

        public static Color Body(CardType type)
        {
            switch (type)
            {
                case CardType.Minion: return new Color(0.55f, 0.38f, 0.22f);
                case CardType.Spell: return new Color(0.24f, 0.4f, 0.72f);
                case CardType.Field: return new Color(0.26f, 0.55f, 0.32f);
                case CardType.Companion: return new Color(0.5f, 0.33f, 0.62f);
                case CardType.Summon: return new Color(0.86f, 0.55f, 0.2f);
                default: return Color.gray;
            }
        }

        public static Color Art(CardType type) => Color.Lerp(Body(type), Color.white, 0.35f);
    }
}
