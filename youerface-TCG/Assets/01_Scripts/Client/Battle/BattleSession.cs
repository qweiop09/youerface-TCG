using System.Collections.Generic;
using TCG.Client.Online;
using TCG.Client.Presentation;
using TCG.Core.AI;
using TCG.Core.Commands;
using TCG.Core.Events;
using TCG.Core.Net;
using TCG.Core.Rules;
using TCG.Core.State;

namespace TCG.Client.Battle
{
    /// <summary>
    /// 대전 1판의 진행: 명령 검증·적용 → 연출 등록, 컴퓨터 차례 진행.
    /// 사람 입력(BattleInput, DecisionPanel)과 컴퓨터가 모두 <see cref="TrySubmit"/>로 명령을 낸다.
    /// </summary>
    public sealed class BattleSession
    {
        readonly IAiPlayer[] _ai;
        readonly IOnlineLink _link;
        bool _awaiting;
        float _waited;
        Command _aiCommand;
        float _aiDelay;

        /// <summary>대전 상태. 온라인이면 받은 시야 상태(변화가 올 때마다 바뀜).</summary>
        public GameState State { get; private set; }
        public GameEngine Engine { get; }
        public PresentationPlayer Player { get; }
        /// <summary>사람이 조종하는 플레이어 (화면 아래쪽).</summary>
        public int Human { get; }
        /// <summary>true면 사람 쪽도 컴퓨터가 둔다 (관전 모드).</summary>
        public bool Spectate { get; set; }
        /// <summary>
        /// 컴퓨터가 행동 전 쉬는 시간(초, 연출 배속 적용). 사람이 무엇을 했는지 따라올 수 있게 종류별로 다르게 둔다.
        /// </summary>
        public AiPacing Pacing { get; } = new AiPacing();

        /// <summary>컴퓨터가 다음 행동을 고른 뒤 기다리는 중인가 ("상대가 생각 중…" 표시용).</summary>
        public bool AiThinking => _aiCommand != null;
        public int CommandsApplied { get; private set; }

        /// <summary>온라인 대전인가 (<see cref="IOnlineLink"/>).</summary>
        public bool Online => _link != null;
        public IOnlineLink Link => _link;
        /// <summary>온라인: 명령을 보내고 결과를 기다리는 중.</summary>
        public bool Awaiting => _awaiting;
        /// <summary>온라인: 변화를 받았을 때 (자동 처리 안내 등).</summary>
        public event System.Action<SeatUpdate> OnlineUpdated;
        /// <summary>온라인: 방장이 명령을 거절했을 때 이유.</summary>
        public event System.Action<string> OnlineRejected;

        public BattleSession(GameState state, GameEngine engine, PresentationPlayer player, int human, IAiPlayer[] ai)
        {
            State = state;
            Engine = engine;
            Player = player;
            Human = human;
            _ai = ai;
        }

        /// <summary>온라인 대전: 첫 시야 상태에서 시작해 상대(방장·손님)와 주고받는다. 컴퓨터는 없다.</summary>
        public BattleSession(IOnlineLink link, GameState view, GameEngine engine, PresentationPlayer player)
        {
            _link = link;
            State = view;
            Engine = engine;
            Player = player;
            Human = link.Seat;
        }

        /// <summary>지금 명령을 내야 하는 플레이어 (선택 대기 중이면 그 선택의 주인).</summary>
        public int Mover => State.Decision?.PlayerIndex ?? State.ActivePlayerIndex;

        public bool InProgress => State.Phase == GamePhase.InProgress;

        /// <summary>사람이 지금 행동할 수 있는가 (내 차례·내 선택이고, 연출이 끝났음).</summary>
        public bool HumanCanAct => InProgress && !Spectate && Mover == Human && !Player.IsPlaying && !_awaiting;

        public bool HumanHasDecision => HumanCanAct && State.Decision != null;

        public ValidationResult Validate(Command command) => Engine.Validate(State, command);

        /// <summary>사람의 명령. 연출 중이거나 규칙상 안 되면 false와 이유.</summary>
        public bool TrySubmit(Command command, out string reason)
        {
            if (Player.IsPlaying)
            {
                reason = "연출이 끝난 뒤에 할 수 있습니다.";
                return false;
            }
            if (_awaiting)
            {
                reason = "상대 응답을 기다리는 중입니다.";
                return false;
            }
            var validation = Engine.Validate(State, command);
            if (!validation.IsValid)
            {
                reason = validation.Reason;
                return false;
            }
            if (Online)
            {
                if (!_link.Submit(command, out reason))
                    return false;
                _awaiting = true;
                TickOnline();
                return true;
            }
            Apply(command);
            reason = null;
            return true;
        }

        void Apply(Command command)
        {
            var before = State.Clone();
            IReadOnlyList<GameEvent> events = Engine.Apply(State, command);
            CommandsApplied++;
            Player.Enqueue(events, before, State.Clone());
        }

        /// <summary>
        /// 매 프레임: 컴퓨터 차례면 (연출이 끝난 뒤) 다음 행동을 먼저 고르고, 행동 종류에 맞는 시간만큼 쉬었다가 둔다.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (Online)
            {
                TickOnline();
                return;
            }
            if (!InProgress || Player.IsPlaying)
                return;
            int mover = Mover;
            if (mover == Human && !Spectate)
            {
                _aiCommand = null;
                return;
            }

            if (_aiCommand == null)
            {
                _aiCommand = _ai[mover].ChooseCommand(State, mover);
                _aiDelay = Pacing.DelayFor(_aiCommand);
                _waited = 0f;
            }

            _waited += deltaTime;
            if (_waited < _aiDelay)
                return;
            var command = _aiCommand;
            _aiCommand = null;
            Apply(command);
        }

        /// <summary>받은 변화를 연출에 넘기고 상태를 바꾼다. 거절이 왔으면 다시 행동할 수 있게.</summary>
        void TickOnline()
        {
            while (_link.TryDequeue(out var update))
            {
                State = update.After;
                _awaiting = false;
                Player.Enqueue(update.Events, update.Before, update.After);
                CommandsApplied++;
                OnlineUpdated?.Invoke(update);
            }
            string rejection = _link.TakeRejection();
            if (rejection != null)
            {
                _awaiting = false;
                OnlineRejected?.Invoke(rejection);
            }
        }

        /// <summary>규칙 엔진 기준으로 이 카드를 내려면 대상을 골라야 하는가.</summary>
        public bool RequiresTarget(Entity card) => Engine.RequiresChosenTarget(card);
    }

    /// <summary>컴퓨터 행동 간격 (초, 연출 배속 적용 전). 연출 길이와 별개로 "다음 행동 전 쉬는 시간".</summary>
    public sealed class AiPacing
    {
        public float PlayCard { get; set; } = 0.75f;
        public float Attack { get; set; } = 0.6f;
        public float EndTurn { get; set; } = 0.7f;
        /// <summary>체인 기회 선택 (올리기·패스).</summary>
        public float ChainChoice { get; set; } = 0.45f;

        public float DelayFor(Command command)
        {
            switch (command)
            {
                case PlayCardCommand _: return PlayCard;
                case AttackCommand _: return Attack;
                case EndTurnCommand _: return EndTurn;
                default: return ChainChoice;
            }
        }
    }
}
