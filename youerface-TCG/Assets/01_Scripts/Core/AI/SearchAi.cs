using System;
using System.Collections.Generic;
using TCG.Core.Commands;
using TCG.Core.Random;
using TCG.Core.Rules;
using TCG.Core.State;

namespace TCG.Core.AI
{
    /// <summary>
    /// 보통·어려움·챌린지: 후보 명령마다 자기 시야(<see cref="AiView"/>)의 복사본에서 결과를 미리 계산하고
    /// <see cref="BoardEvaluator"/> 점수가 가장 높은 것을 고른다. 난이도는 <see cref="Settings"/>로만 다르다.
    /// - 보통: 행동 1개의 결과만 본다.
    /// - 어려움·챌린지: 상위 후보마다 내 턴을 끝까지 진행하고, 상대의 다음 턴(상대도 같은 방식으로 둔다고 가정)까지
    ///   돌려 본 뒤 평가한다. 챌린지는 가정을 더 여러 번 하고 상대 손패를 본다.
    /// </summary>
    public sealed class SearchAi : IAiPlayer
    {
        public sealed class Settings
        {
            /// <summary>숨긴 정보를 몇 가지로 가정해 평균 낼지.</summary>
            public int Samples { get; set; } = 1;
            /// <summary>상대 턴까지 돌려 볼 상위 후보 수 (OpponentReply일 때).</summary>
            public int ExpandTop { get; set; } = 6;
            /// <summary>내 턴 끝까지 + 상대의 다음 턴까지 시뮬레이션해서 평가하는가.</summary>
            public bool OpponentReply { get; set; }
            /// <summary>시뮬레이션에서 한 턴에 진행할 최대 행동 수.</summary>
            public int MaxRolloutSteps { get; set; } = 12;
            /// <summary>다음 턴 치명타 위험·기회를 점수에 넣는가.</summary>
            public bool ThreatAware { get; set; }
            /// <summary>상대 손패를 보는가 (챌린지).</summary>
            public bool RevealOpponentHand { get; set; }
        }

        /// <summary>판단 기록 (테스트 배틀의 "AI 판단 이유 표시"용).</summary>
        public sealed class CandidateScore
        {
            public Command Command { get; }
            public long Score { get; }
            public CandidateScore(Command command, long score) { Command = command; Score = score; }
        }

        const int MaxAutoDecisions = 64;

        readonly GameEngine _engine;
        readonly DeterministicRng _rng;
        readonly Settings _settings;

        public IReadOnlyList<CandidateScore> LastScores { get; private set; } = new CandidateScore[0];

        public SearchAi(GameEngine engine, int seed, Settings settings)
        {
            _engine = engine;
            _rng = new DeterministicRng(seed);
            _settings = settings;
        }

        public Command ChooseCommand(GameState state, int playerIndex)
        {
            var candidates = LegalCommands.Generate(_engine, state, playerIndex);
            if (candidates.Count == 0)
                throw new InvalidOperationException("No legal command for this player now.");
            if (candidates.Count == 1)
            {
                LastScores = new[] { new CandidateScore(candidates[0], 0) };
                return candidates[0];
            }

            var totals = new long[candidates.Count];
            var considered = new bool[candidates.Count];
            for (int s = 0; s < _settings.Samples; s++)
            {
                var view = AiView.Create(state, playerIndex, _settings.RevealOpponentHand, _rng);
                var scores = ScoreCandidates(view, candidates, playerIndex, considered);
                for (int i = 0; i < totals.Length; i++)
                    totals[i] += scores[i];
            }

            int best = -1;
            var record = new List<CandidateScore>();
            for (int i = 0; i < candidates.Count; i++)
            {
                if (!considered[i])
                    continue;
                record.Add(new CandidateScore(candidates[i], totals[i]));
                if (best < 0 || totals[i] > totals[best])
                    best = i;
            }
            LastScores = record;
            return candidates[best];
        }

        /// <summary>
        /// 후보별 점수. OpponentReply면 1차 점수 상위 ExpandTop개(+턴 종료)만 상대 턴까지 돌려 보고,
        /// 나머지는 considered=false로 남긴다 (점수 척도가 달라 섞어 비교하지 않는다).
        /// </summary>
        long[] ScoreCandidates(GameState view, List<Command> candidates, int me, bool[] considered)
        {
            var scores = new long[candidates.Count];
            var results = new GameState[candidates.Count];
            for (int i = 0; i < candidates.Count; i++)
            {
                results[i] = Simulate(view, candidates[i]);
                scores[i] = BoardEvaluator.Evaluate(results[i], me, _settings.ThreatAware);
            }

            if (!_settings.OpponentReply)
            {
                for (int i = 0; i < candidates.Count; i++)
                    considered[i] = true;
                return scores;
            }

            var order = new List<int>();
            for (int i = 0; i < candidates.Count; i++)
                order.Add(i);
            order.Sort((a, b) => scores[b] != scores[a] ? scores[b].CompareTo(scores[a]) : a.CompareTo(b));

            var expand = new List<int>();
            for (int k = 0; k < order.Count && expand.Count < _settings.ExpandTop; k++)
                expand.Add(order[k]);
            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i] is EndTurnCommand && !expand.Contains(i))
                    expand.Add(i);
            }

            for (int k = 0; k < expand.Count; k++)
            {
                int i = expand[k];
                considered[i] = true;
                var after = results[i];
                if (after.Phase == GamePhase.InProgress && AiPlayers.IsToMove(after, me))
                    after = GreedyRollout(after, me);
                if (after.Phase == GamePhase.InProgress)
                    after = GreedyRollout(after, 1 - me);
                scores[i] = BoardEvaluator.Evaluate(after, me, _settings.ThreatAware);
            }
            return scores;
        }

        /// <summary>mover가 자기 차례인 동안 1수 앞만 보는 방식(보통 난이도)으로 두고, 턴을 넘긴 상태를 반환한다.</summary>
        GameState GreedyRollout(GameState state, int mover)
        {
            for (int step = 0; step < _settings.MaxRolloutSteps; step++)
            {
                if (state.Phase != GamePhase.InProgress || !AiPlayers.IsToMove(state, mover))
                    return state;

                var candidates = LegalCommands.Generate(_engine, state, mover);
                GameState bestState = null;
                int bestScore = int.MinValue;
                bool bestIsEnd = false;
                for (int i = 0; i < candidates.Count; i++)
                {
                    var next = Simulate(state, candidates[i]);
                    int score = BoardEvaluator.Evaluate(next, mover, false);
                    if (bestState == null || score > bestScore)
                    {
                        bestState = next;
                        bestScore = score;
                        bestIsEnd = candidates[i] is EndTurnCommand;
                    }
                }

                if (bestState == null)
                    return state;
                state = bestState;
                if (bestIsEnd)
                    return state;
            }

            if (state.Phase == GamePhase.InProgress && state.Decision == null && state.ActivePlayerIndex == mover)
                state = Simulate(state, new EndTurnCommand(mover));
            return state;
        }

        /// <summary>복사본에 명령을 적용하고, 이어지는 선택 요청은 기본 응답으로 넘긴다.</summary>
        GameState Simulate(GameState from, Command command)
        {
            var sim = from.Clone();
            if (!_engine.Validate(sim, command).IsValid)
                return sim;
            _engine.Apply(sim, command);
            for (int i = 0; i < MaxAutoDecisions && sim.Decision != null && sim.Phase == GamePhase.InProgress; i++)
                _engine.Apply(sim, LegalCommands.DefaultAnswer(sim.Decision));
            return sim;
        }
    }
}
