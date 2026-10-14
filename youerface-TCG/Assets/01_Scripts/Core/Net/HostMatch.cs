using System;
using System.Collections.Generic;
using TCG.Core.Commands;
using TCG.Core.Data;
using TCG.Core.Effects;
using TCG.Core.Events;
using TCG.Core.Rules;
using TCG.Core.State;

namespace TCG.Core.Net
{
    /// <summary>자동 처리 이유 (GameDesign §8.1).</summary>
    public enum AutoReason
    {
        None,
        /// <summary>턴·선택 시간 초과.</summary>
        Timeout,
        /// <summary>연결이 끊긴 플레이어의 차례에서 기다린 뒤.</summary>
        Disconnected,
    }

    public enum MatchEndReason
    {
        None,
        /// <summary>영웅 체력으로 끝남.</summary>
        Normal,
        Conceded,
        /// <summary>연속 시간 초과로 패배.</summary>
        Timeouts,
        /// <summary>끊긴 채 자동 선택이 반복돼 패배.</summary>
        Disconnected,
        /// <summary>(게스트 쪽) 방장이 돌아오지 않아 끝남. 승패 미정(Q13) — 지금은 무효.</summary>
        HostLost,
    }

    /// <summary>시간·연결 상태. 받은 쪽은 받은 시점부터 RemainingMs를 줄여 표시한다.</summary>
    public sealed class MatchClock
    {
        /// <summary>지금 행동할 플레이어. 대전이 끝났으면 -1.</summary>
        public int Mover { get; set; } = -1;
        /// <summary>체인 선택 시간인가 (아니면 턴 시간).</summary>
        public bool Deciding { get; set; }
        public int RemainingMs { get; set; }
        /// <summary>행동할 플레이어가 끊겼으면 자동 선택까지 남은 시간, 아니면 0.</summary>
        public int DisconnectRemainingMs { get; set; }
        public int[] Timeouts { get; set; } = new int[GameState.PlayerCount];
        public int[] DisconnectAutos { get; set; } = new int[GameState.PlayerCount];
        public bool[] Connected { get; set; } = { true, true };
    }

    /// <summary>한 플레이어에게 보이는 변화 1번 (명령 1개 또는 자동 처리 1개).</summary>
    public sealed class SeatUpdate
    {
        public int Seat { get; set; }
        public IReadOnlyList<GameEvent> Events { get; set; }
        /// <summary>시야 상태(이번에 공개된 카드는 앞면으로 바꾼 것).</summary>
        public GameState Before { get; set; }
        public GameState After { get; set; }
        public AutoReason Auto { get; set; }
        /// <summary>자동 처리된 플레이어 (Auto가 None이면 -1).</summary>
        public int AutoPlayer { get; set; } = -1;
        public MatchEndReason End { get; set; }
        public MatchClock Clock { get; set; }
        /// <summary>재접속으로 상태 전체를 다시 받음 (이벤트 없음, 화면을 새로 그린다).</summary>
        public bool Resync { get; set; }
    }

    /// <summary>
    /// 호스트 판정 대전 (Architecture §6): 진짜 상태와 엔진을 가지고, 명령 검증 → 적용 → 자리별 시야 변화를 낸다.
    /// 턴·선택 타이머와 끊긴 플레이어 자동 선택도 여기서 (GameDesign §8.1). 시간은 정수 밀리초로 받는다.
    /// 네트워크와 무관: 보내기는 <see cref="Updated"/>를 받는 쪽(<see cref="OnlineHost"/>)이 한다.
    /// </summary>
    public sealed class HostMatch
    {
        readonly IReadOnlyList<CardDefinition>[] _decks;
        readonly CardDefinition _bonusCard;
        int _turnMs, _decisionMs, _disconnectMs;
        int _clockTurn = -1, _clockMover = -1;
        PendingDecision _clockDecision;

        public GameState State { get; }
        public GameEngine Engine { get; }
        public MatchCatalog Catalog { get; } = new MatchCatalog();
        public RuleConfig Rules => State.Rules;
        public bool[] Connected { get; } = { true, true };
        public int[] Timeouts { get; } = new int[GameState.PlayerCount];
        public int[] DisconnectAutos { get; } = new int[GameState.PlayerCount];
        public MatchEndReason EndReason { get; private set; }
        public int CommandsApplied { get; private set; }

        /// <summary>자리별 변화. 명령 1개마다 자리 0, 1 순서로 한 번씩.</summary>
        public event Action<SeatUpdate> Updated;
        /// <summary>연결 상태만 바뀌었을 때.</summary>
        public event Action<MatchClock> ClockChanged;

        public HostMatch(RuleConfig rules, int seed, IReadOnlyList<CardDefinition> deck0, IReadOnlyList<CardDefinition> deck1,
            EffectRegistry registry = null, CardDefinition bonusCard = null)
        {
            State = new GameState(rules ?? RuleConfig.CreateDefault(), seed);
            Engine = new GameEngine(registry);
            _decks = new[] { deck0, deck1 };
            _bonusCard = bonusCard;
            Catalog.AddRange(deck0);
            Catalog.AddRange(deck1);
            if (bonusCard != null)
                Catalog.Add(bonusCard);
        }

        public bool InProgress => State.Phase == GamePhase.InProgress;

        /// <summary>지금 명령을 내야 하는 플레이어. 끝났으면 -1.</summary>
        public int Mover => !InProgress ? -1 : State.Decision?.PlayerIndex ?? State.ActivePlayerIndex;

        public void Start()
        {
            var before = State.Clone();
            var events = Engine.StartGame(State, _decks[0], _decks[1], _bonusCard);
            Publish(before, events, AutoReason.None, -1);
        }

        /// <summary>자리 seat의 명령. 자리가 다르거나 규칙상 안 되면 실패와 이유.</summary>
        public ValidationResult Submit(int seat, Command command)
        {
            if (command == null || command.PlayerIndex != seat)
                return ValidationResult.Fail("잘못된 요청입니다.");
            var validation = Engine.Validate(State, command);
            if (!validation.IsValid)
                return validation;
            Timeouts[seat] = 0;
            DisconnectAutos[seat] = 0;
            Apply(command, AutoReason.None, command is ConcedeCommand ? MatchEndReason.Conceded : MatchEndReason.Normal);
            return ValidationResult.Ok;
        }

        public void SetConnected(int seat, bool connected)
        {
            if (Connected[seat] == connected)
                return;
            Connected[seat] = connected;
            _disconnectMs = Rules.DisconnectedActionWaitSeconds * 1000;
            ClockChanged?.Invoke(Clock());
        }

        /// <summary>시간 진행. 시간 초과·끊김 대기가 끝나면 자동 선택(또는 패배)을 적용한다.</summary>
        public void Tick(int elapsedMs)
        {
            if (!InProgress || elapsedMs <= 0)
                return;
            int mover = Mover;
            bool deciding = State.Decision != null;
            if (deciding)
                _decisionMs -= elapsedMs;
            else
                _turnMs -= elapsedMs;
            if (!Connected[mover])
                _disconnectMs -= elapsedMs;

            if ((deciding ? _decisionMs : _turnMs) <= 0)
                AutoAct(mover, AutoReason.Timeout);
            else if (!Connected[mover] && _disconnectMs <= 0)
                AutoAct(mover, AutoReason.Disconnected);
        }

        public MatchClock Clock()
        {
            int mover = Mover;
            bool deciding = State.Decision != null;
            return new MatchClock
            {
                Mover = mover,
                Deciding = deciding,
                RemainingMs = mover < 0 ? 0 : Math.Max(0, deciding ? _decisionMs : _turnMs),
                DisconnectRemainingMs = mover >= 0 && !Connected[mover] ? Math.Max(0, _disconnectMs) : 0,
                Timeouts = (int[])Timeouts.Clone(),
                DisconnectAutos = (int[])DisconnectAutos.Clone(),
                Connected = (bool[])Connected.Clone(),
            };
        }

        void AutoAct(int player, AutoReason reason)
        {
            bool timeout = reason == AutoReason.Timeout;
            int count = timeout ? ++Timeouts[player] : ++DisconnectAutos[player];
            int limit = timeout ? Rules.MaxConsecutiveTimeouts : Rules.MaxDisconnectedAutoActions;
            _disconnectMs = Rules.DisconnectedActionWaitSeconds * 1000;
            if (count >= limit)
            {
                Apply(new ConcedeCommand(player), reason, timeout ? MatchEndReason.Timeouts : MatchEndReason.Disconnected);
                return;
            }
            Apply(AutoCommand(State, player), reason, MatchEndReason.Normal);
        }

        /// <summary>자동 선택: 체인 기회면 패스(못 하면 첫 번째 효과), 아니면 턴 종료.</summary>
        public static Command AutoCommand(GameState state, int player)
        {
            var decision = state.Decision;
            if (decision == null)
                return new EndTurnCommand(player);
            return decision.CanPass || decision.Options.Count == 0
                ? (Command)new PassChainCommand(player)
                : new ChainActivateCommand(player, decision.Options[0]);
        }

        void Apply(Command command, AutoReason auto, MatchEndReason endReason)
        {
            var before = State.Clone();
            var events = Engine.Apply(State, command);
            CommandsApplied++;
            if (State.Phase == GamePhase.Finished && EndReason == MatchEndReason.None)
                EndReason = endReason;
            Publish(before, events, auto, auto == AutoReason.None ? -1 : command.PlayerIndex);
        }

        void Publish(GameState before, IReadOnlyList<GameEvent> events, AutoReason auto, int autoPlayer)
        {
            if (State.Phase == GamePhase.Finished && EndReason == MatchEndReason.None)
                EndReason = MatchEndReason.Normal;
            RefreshClocks();
            var clock = Clock();
            for (int seat = 0; seat < GameState.PlayerCount; seat++)
            {
                var after = PlayerView.Create(State, seat);
                Updated?.Invoke(new SeatUpdate
                {
                    Seat = seat,
                    Events = PlayerView.FilterEvents(events, after),
                    Before = PlayerView.Reveal(PlayerView.Create(before, seat), after),
                    After = after,
                    Auto = auto,
                    AutoPlayer = autoPlayer,
                    End = EndReason,
                    Clock = clock,
                });
            }
        }

        /// <summary>새 턴이면 턴 시간, 새 선택이면 선택 시간, 행동할 사람이 바뀌면 끊김 대기 시간을 다시 채운다.</summary>
        void RefreshClocks()
        {
            if (State.TurnNumber != _clockTurn)
            {
                _clockTurn = State.TurnNumber;
                _turnMs = Rules.TurnTimeLimitSeconds * 1000;
            }
            if (!ReferenceEquals(State.Decision, _clockDecision))
            {
                _clockDecision = State.Decision;
                if (_clockDecision != null)
                    _decisionMs = Rules.DecisionTimeLimitSeconds * 1000;
            }
            if (Mover != _clockMover)
            {
                _clockMover = Mover;
                _disconnectMs = Rules.DisconnectedActionWaitSeconds * 1000;
            }
        }
    }
}
