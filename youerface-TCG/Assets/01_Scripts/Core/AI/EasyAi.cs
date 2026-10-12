using System.Collections.Generic;
using TCG.Core.Commands;
using TCG.Core.Random;
using TCG.Core.Rules;
using TCG.Core.State;

namespace TCG.Core.AI
{
    /// <summary>쉬움: 계산하지 않고 낼 수 있는 카드와 공격을 무작위로 고른다. 가끔 할 일이 남아도 턴을 넘긴다.</summary>
    public sealed class EasyAi : IAiPlayer
    {
        readonly GameEngine _engine;
        readonly DeterministicRng _rng;

        public EasyAi(GameEngine engine, int seed)
        {
            _engine = engine;
            _rng = new DeterministicRng(seed);
        }

        public Command ChooseCommand(GameState state, int playerIndex)
        {
            var candidates = LegalCommands.Generate(_engine, state, playerIndex);
            if (candidates.Count == 0)
                throw new System.InvalidOperationException("No legal command for this player now.");
            if (state.Decision != null)
                return candidates[_rng.Next(candidates.Count)];

            var plays = new List<Command>();
            var attacks = new List<Command>();
            Command endTurn = null;
            for (int i = 0; i < candidates.Count; i++)
            {
                switch (candidates[i])
                {
                    case PlayCardCommand _: plays.Add(candidates[i]); break;
                    case AttackCommand _: attacks.Add(candidates[i]); break;
                    case EndTurnCommand _: endTurn = candidates[i]; break;
                }
            }

            if ((plays.Count > 0 || attacks.Count > 0) && _rng.Next(100) < 8)
                return endTurn;
            if (plays.Count > 0 && _rng.Next(100) < 75)
                return plays[_rng.Next(plays.Count)];
            if (attacks.Count > 0)
                return attacks[_rng.Next(attacks.Count)];
            return endTurn;
        }
    }
}
