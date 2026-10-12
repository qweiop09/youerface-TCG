using System.Collections.Generic;
using TCG.Core.Commands;
using TCG.Core.Rules;
using TCG.Core.State;

namespace TCG.Core.AI
{
    /// <summary>지금 낼 수 있는 명령 목록 (결정적 순서). 엔진 검증을 통과한 것만.</summary>
    public static class LegalCommands
    {
        /// <summary>선택 효과·순서 조합이 이보다 많으면 일부(제시 순서, 역순)만 만든다.</summary>
        const int MaxPermutedItems = 3;

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
            if (decision.Type == DecisionType.ChainResponse)
            {
                result.Add(new PassChainCommand(decision.PlayerIndex));
                return;
            }

            var mandatory = new List<int>();
            var optional = new List<int>();
            for (int i = 0; i < decision.Options.Count; i++)
            {
                int id = decision.Options[i];
                var trigger = state.PendingTriggers.Find(t => t.Id == id);
                if (trigger != null && trigger.IsOptional)
                    optional.Add(id);
                else
                    mandatory.Add(id);
            }

            var subsets = new List<List<int>>();
            if (optional.Count <= MaxPermutedItems)
            {
                for (int mask = (1 << optional.Count) - 1; mask >= 0; mask--)
                {
                    var subset = new List<int>();
                    for (int i = 0; i < optional.Count; i++)
                    {
                        if ((mask & (1 << i)) != 0)
                            subset.Add(optional[i]);
                    }
                    subsets.Add(subset);
                }
            }
            else
            {
                subsets.Add(new List<int>(optional));
                subsets.Add(new List<int>());
            }

            for (int s = 0; s < subsets.Count; s++)
            {
                var items = new List<int>();
                for (int i = 0; i < decision.Options.Count; i++)
                {
                    int id = decision.Options[i];
                    if (mandatory.Contains(id) || subsets[s].Contains(id))
                        items.Add(id);
                }

                if (items.Count <= MaxPermutedItems)
                {
                    foreach (var order in Permutations(items))
                        result.Add(new ChooseTriggersCommand(decision.PlayerIndex, order));
                }
                else
                {
                    result.Add(new ChooseTriggersCommand(decision.PlayerIndex, items));
                    var reversed = new List<int>(items);
                    reversed.Reverse();
                    result.Add(new ChooseTriggersCommand(decision.PlayerIndex, reversed));
                }
            }
        }

        static IEnumerable<List<int>> Permutations(List<int> items)
        {
            if (items.Count <= 1)
            {
                yield return new List<int>(items);
                yield break;
            }
            for (int i = 0; i < items.Count; i++)
            {
                var rest = new List<int>(items);
                rest.RemoveAt(i);
                foreach (var tail in Permutations(rest))
                {
                    tail.Insert(0, items[i]);
                    yield return tail;
                }
            }
        }

        /// <summary>기본 응답: 제시된 순서대로 전부 발동, 대응은 패스.</summary>
        public static Command DefaultAnswer(PendingDecision decision)
        {
            if (decision.Type == DecisionType.ChooseTriggers)
                return new ChooseTriggersCommand(decision.PlayerIndex, new List<int>(decision.Options));
            return new PassChainCommand(decision.PlayerIndex);
        }
    }
}
