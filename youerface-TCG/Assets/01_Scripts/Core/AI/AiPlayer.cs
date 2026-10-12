using System;
using TCG.Core.Commands;
using TCG.Core.Rules;
using TCG.Core.State;

namespace TCG.Core.AI
{
    /// <summary>싱글 배틀 난이도 (GameDesign §3.4). 실력(판단력) 차이만 낸다.</summary>
    public enum AiDifficulty
    {
        /// <summary>쉬움: 낼 수 있는 카드·공격을 대충 고른다.</summary>
        Easy,
        /// <summary>보통: 행동 하나씩 결과를 계산해 가장 좋은 것을 고른다.</summary>
        Normal,
        /// <summary>어려움: 내 턴 끝까지와 상대의 다음 턴 대응까지 시뮬레이션해 본다.</summary>
        Hard,
        /// <summary>챌린지: 어려움보다 더 많은 후보·가정을 보고, 플레이어 손패를 본다(덱 순서는 못 봄).</summary>
        Challenge,
    }

    /// <summary>
    /// 컴퓨터 플레이어. 상태를 직접 바꾸지 않고 다음 명령만 고른다.
    /// 자기 차례이거나 자기에게 걸린 선택(체인 순서 등)이 있을 때만 부른다.
    /// </summary>
    public interface IAiPlayer
    {
        Command ChooseCommand(GameState state, int playerIndex);
    }

    public static class AiPlayers
    {
        public static IAiPlayer Create(AiDifficulty difficulty, int seed, GameEngine engine = null)
        {
            engine = engine ?? new GameEngine();
            switch (difficulty)
            {
                case AiDifficulty.Easy:
                    return new EasyAi(engine, seed);
                case AiDifficulty.Normal:
                    return new SearchAi(engine, seed, new SearchAi.Settings { Samples = 1 });
                case AiDifficulty.Hard:
                    return new SearchAi(engine, seed, new SearchAi.Settings { Samples = 1, OpponentReply = true, ExpandTop = 5, ThreatAware = true });
                case AiDifficulty.Challenge:
                    return new SearchAi(engine, seed, new SearchAi.Settings { Samples = 3, OpponentReply = true, ExpandTop = 8, ThreatAware = true, RevealOpponentHand = true });
                default:
                    throw new ArgumentOutOfRangeException(nameof(difficulty), difficulty, null);
            }
        }

        /// <summary>이 플레이어가 지금 명령을 내야 하는가 (자기 턴, 또는 자기에게 걸린 선택).</summary>
        public static bool IsToMove(GameState state, int playerIndex)
        {
            if (state.Phase != GamePhase.InProgress)
                return false;
            if (state.Decision != null)
                return state.Decision.PlayerIndex == playerIndex;
            return state.ActivePlayerIndex == playerIndex;
        }
    }
}
