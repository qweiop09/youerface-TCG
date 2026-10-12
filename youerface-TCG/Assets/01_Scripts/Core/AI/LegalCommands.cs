using System.Collections.Generic;
using TCG.Core.Commands;
using TCG.Core.Rules;
using TCG.Core.State;

namespace TCG.Core.AI
{
    /// <summary>지금 낼 수 있는 명령 목록 (결정적 순서). 엔진 검증을 통과한 것만.</summary>
    public static class LegalCommands
    {
        public static List<Command> Generate(GameEngine engine, GameState state, int player)
        {
            var result = new List<Command>();
            if (state.Phase != GamePhase.InProgress)
                return result;

            if (state.Decision != null)
            {
                if (state.Decision.PlayerIndex == player)
                    AddDecisionAnswers(state, state.Decision, result);
                return result;
            }

            if (state.ActivePlayerIndex != player)
                return result;

            var me = state.GetPlayer(player);
            var opponent = state.GetOpponent(player);

            var hand = new List<Entity>(me.Hand.Entities);
            for (int i = 0; i < hand.Count; i++)
            {
                var card = hand[i];
                if (engine.RequiresChosenTarget(card))
                {
                    var targets = EffectTargets(state);
                    for (int t = 0; t < targets.Count; t++)
                        AddIfValid(engine, state, new PlayCardCommand(player, card.Id, targets[t]), result);
                }
                else
                {
                    AddIfValid(engine, state, new PlayCardCommand(player, card.Id), result);
                }
            }

            var attackTargets = new List<int> { opponent.Hero.Id };
            for (int i = 0; i < opponent.Minions.Count; i++)
                attackTargets.Add(opponent.Minions.Entities[i].Id);
            for (int i = 0; i < me.Minions.Count; i++)
            {
                int attacker = me.Minions.Entities[i].Id;
                for (int t = 0; t < attackTargets.Count; t++)
                    AddIfValid(engine, state, new AttackCommand(player, attacker, attackTargets[t]), result);
            }

            result.Add(new EndTurnCommand(player));
            return result;
        }

        /// <summary>효과 대상이 될 수 있는 모든 ID: 영웅 2명, 그다음 양쪽 보드 카드.</summary>
        public static List<int> EffectTargets(GameState state)
        {
            var targets = new List<int>();
            for (int p = 0; p < GameState.PlayerCount; p++)
                targets.Add(state.GetPlayer(p).Hero.Id);
            for (int p = 0; p < GameState.PlayerCount; p++)
            {
                var zones = state.GetPlayer(p).BoardZones;
                for (int z = 0; z < zones.Count; z++)
                {
                    for (int i = 0; i < zones[z].Count; i++)
                        targets.Add(zones[z].Entities[i].Id);
                }
            }
            return targets;
        }

        static void AddIfValid(GameEngine engine, GameState state, Command command, List<Command> result)
        {
            if (engine.Validate(state, command).IsValid)
                result.Add(command);
        }

        static void AddDecisionAnswers(GameState state, PendingDecision decision, List<Command> result)
        {
            for (int i = 0; i < decision.Options.Count; i++)
                result.Add(new ChainActivateCommand(decision.PlayerIndex, decision.Options[i]));
            if (decision.CanPass)
                result.Add(new PassChainCommand(decision.PlayerIndex));
        }

        /// <summary>기본 응답: 제시된 첫 효과를 체인에 올린다. 올릴 것이 없으면 패스.</summary>
        public static Command DefaultAnswer(PendingDecision decision)
        {
            if (decision.Options.Count > 0)
                return new ChainActivateCommand(decision.PlayerIndex, decision.Options[0]);
            return new PassChainCommand(decision.PlayerIndex);
        }
    }
}
