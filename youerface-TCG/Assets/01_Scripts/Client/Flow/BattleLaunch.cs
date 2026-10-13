using TCG.Core.AI;
using UnityEngine.SceneManagement;

namespace TCG.Client.Flow
{
    /// <summary>
    /// 싱글 배틀 흐름: 시작 화면(Title) → 대전(Battle) → 결과 → 다시 하기 / 난이도 선택으로.
    /// 장면 사이에 고른 난이도와 시드를 넘긴다. Battle 장면을 바로 열면(요청 없음) 장면에 저장된 설정으로 시작한다.
    /// </summary>
    public static class BattleLaunch
    {
        public const string TitleScene = "Title";
        public const string BattleScene = "Battle";

        /// <summary>마지막으로 고른 난이도 (시작 화면이 이 값을 기본 선택으로 보여 준다).</summary>
        public static AiDifficulty Difficulty { get; set; } = AiDifficulty.Normal;

        static bool _pending;
        static int _seed;

        /// <summary>고른 난이도로 새 대전을 연다. 시드를 안 주면 매번 다르게.</summary>
        public static void StartBattle(AiDifficulty difficulty, int? seed = null)
        {
            Difficulty = difficulty;
            _seed = seed ?? NewSeed();
            _pending = true;
            SceneManager.LoadScene(BattleScene);
        }

        /// <summary>같은 난이도로 새 판 (시드는 새로).</summary>
        public static void Rematch() => StartBattle(Difficulty);

        public static void ToTitle() => SceneManager.LoadScene(TitleScene);

        /// <summary>Battle 장면 시작 시 한 번 읽는다. 요청이 없으면 false.</summary>
        public static bool TryConsume(out AiDifficulty difficulty, out int seed)
        {
            difficulty = Difficulty;
            seed = _seed;
            if (!_pending)
                return false;
            _pending = false;
            return true;
        }

        static int NewSeed() => unchecked((int)System.DateTime.Now.Ticks) & int.MaxValue;
    }
}
