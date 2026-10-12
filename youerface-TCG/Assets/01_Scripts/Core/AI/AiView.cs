using TCG.Core.Random;
using TCG.Core.State;

namespace TCG.Core.AI
{
    /// <summary>
    /// AI가 미리 계산해 볼 때 쓰는 "자기 눈에 보이는 세계"의 복사본 (반칙 방지).
    /// - 난수 상태를 새로 정해 실제 무작위 결과를 미리 알 수 없게 한다.
    /// - 자기 덱 순서를 섞는다 (자기도 다음에 뭘 뽑을지 모른다).
    /// - 상대 손패와 덱을 한데 섞어 다시 나눈다 (상대 손패를 모른다). 손패 공개(챌린지)면 덱만 섞는다.
    /// 상대 덱 구성(어떤 카드들이 들어 있는지)은 안다고 가정한다.
    /// </summary>
    public static class AiView
    {
        public static GameState Create(GameState state, int viewer, bool revealOpponentHand, DeterministicRng rng)
        {
            var view = state.Clone();
            view.Rng.RestoreState(new RngState(rng.NextUInt() | 1u, rng.NextUInt(), rng.NextUInt(), rng.NextUInt()));

            view.GetPlayer(viewer).Deck.Shuffle(rng);

            var opponent = view.GetOpponent(viewer);
            if (revealOpponentHand)
            {
                opponent.Deck.Shuffle(rng);
                return view;
            }

            int handCount = opponent.Hand.Count;
            var pool = new System.Collections.Generic.List<Entity>(opponent.Hand.Entities);
            pool.AddRange(opponent.Deck.Entities);
            for (int i = 0; i < pool.Count; i++)
            {
                if (!opponent.Hand.Remove(pool[i]))
                    opponent.Deck.Remove(pool[i]);
            }
            rng.Shuffle(pool);
            for (int i = 0; i < pool.Count; i++)
            {
                if (i < handCount)
                    opponent.Hand.TryAdd(pool[i]);
                else
                    opponent.Deck.TryAdd(pool[i]);
            }
            return view;
        }
    }
}
