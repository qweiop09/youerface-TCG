using TCG.Core.Data;
using TCG.Core.State;

namespace TCG.Core.AI
{
    /// <summary>
    /// 상태가 한 플레이어에게 얼마나 유리한지 정수 점수로 매긴다 (AI 전용, 카드 "점수"와는 다른 개념).
    /// 가중치는 AI 튜닝용 임시값이며, 테스트 배틀의 AI 자동 대전으로 조정한다.
    /// </summary>
    public static class BoardEvaluator
    {
        public const int Win = 1000000;

        public static int Evaluate(GameState state, int me, bool threatAware)
        {
            if (state.Phase == GamePhase.Finished)
            {
                if (state.WinnerIndex == null)
                    return 0;
                return state.WinnerIndex == me ? Win : -Win;
            }

            var mine = state.GetPlayer(me);
            var theirs = state.GetOpponent(me);

            int score = HeroValue(mine.Hero.Health) - HeroValue(theirs.Hero.Health);
            score += SideValue(mine) - SideValue(theirs);
            score += (mine.Hand.Count - theirs.Hand.Count) * 5;

            if (threatAware)
            {
                if (BoardAttack(theirs) >= mine.Hero.Health + TauntHealth(mine))
                    score -= 400;
                if (BoardAttack(mine) >= theirs.Hero.Health + TauntHealth(theirs))
                    score += 200;
            }
            return score;
        }

        /// <summary>체력이 낮을수록 1점의 가치가 커진다.</summary>
        static int HeroValue(int health)
        {
            if (health <= 0)
                return -Win / 10;
            int low = health < 10 ? health : 10;
            return health * 6 + low * 6;
        }

        static int SideValue(PlayerState player)
        {
            int value = 0;
            var minions = player.Minions.Entities;
            for (int i = 0; i < minions.Count; i++)
            {
                var m = minions[i];
                value += (m.Attack ?? 0) * 6 + (m.Health ?? 0) * 4 + 4;
                if (m.HasKeyword(Keyword.Taunt)) value += 4;
                if (m.HasKeyword(Keyword.Stealth)) value += 3;
                if (m.HasKeyword(Keyword.TreasureChest)) value += 3;
                value += EffectCount(m) * 3;
            }

            for (int i = 0; i < player.Field.Count; i++)
                value += 8 + EffectCount(player.Field.Entities[i]) * 4;

            for (int i = 0; i < player.Companions.Count; i++)
            {
                var c = player.Companions.Entities[i];
                value += 6 + EffectCount(c) * (c.Usage.HasValue ? c.Usage.Value * 2 : 6);
            }

            for (int i = 0; i < player.Summons.Count; i++)
            {
                var s = player.Summons.Entities[i];
                value += 4 + (s.Usage ?? 0) * 4 + EffectCount(s) * 2;
            }
            return value;
        }

        static int EffectCount(Entity e) => e.Definition.Effects?.Count ?? 0;

        static int BoardAttack(PlayerState player)
        {
            int sum = 0;
            for (int i = 0; i < player.Minions.Count; i++)
                sum += player.Minions.Entities[i].Attack ?? 0;
            return sum;
        }

        static int TauntHealth(PlayerState player)
        {
            int sum = 0;
            for (int i = 0; i < player.Minions.Count; i++)
            {
                var m = player.Minions.Entities[i];
                if (m.HasKeyword(Keyword.Taunt) && !m.HasKeyword(Keyword.Stealth))
                    sum += m.Health ?? 0;
            }
            return sum;
        }
    }
}
