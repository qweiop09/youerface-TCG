using System.Collections.Generic;
using TCG.Client.Presentation;
using TCG.Core.AI;
using TCG.Core.Commands;
using TCG.Core.Events;
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
        float _waited;

        public GameState State { get; }
        public GameEngine Engine { get; }
        public PresentationPlayer Player { get; }
        /// <summary>사람이 조종하는 플레이어 (화면 아래쪽).</summary>
        public int Human { get; }
        /// <summary>true면 사람 쪽도 컴퓨터가 둔다 (관전 모드).</summary>
        public bool Spectate { get; set; }
        /// <summary>컴퓨터가 한 수를 두기 전 기다리는 시간(초, 연출 배속 적용).</summary>
        public float ThinkDelay { get; set; } = 0.35f;
        public int CommandsApplied { get; private set; }

        public BattleSession(GameState state, GameEngine engine, PresentationPlayer player, int human, IAiPlayer[] ai)
        {
            State = state;
            Engine = engine;
            Player = player;
            Human = human;
            _ai = ai;
        }

        /// <summary>지금 명령을 내야 하는 플레이어 (선택 대기 중이면 그 선택의 주인).</summary>
        public int Mover => State.Decision?.PlayerIndex ?? State.ActivePlayerIndex;

        public bool InProgress => State.Phase == GamePhase.InProgress;

        /// <summary>사람이 지금 행동할 수 있는가 (내 차례·내 선택이고, 연출이 끝났음).</summary>
        public bool HumanCanAct => InProgress && !Spectate && Mover == Human && !Player.IsPlaying;

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
            var validation = Engine.Validate(State, command);
            if (!validation.IsValid)
            {
                reason = validation.Reason;
                return false;
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

        /// <summary>매 프레임: 컴퓨터 차례면 연출이 끝난 뒤 잠깐 기다렸다가 한 수 둔다.</summary>
        public void Tick(float deltaTime)
        {
            if (!InProgress || Player.IsPlaying)
                return;
            int mover = Mover;
            if (mover == Human && !Spectate)
                return;

            _waited += deltaTime;
            if (_waited < ThinkDelay)
                return;
            _waited = 0f;
            Apply(_ai[mover].ChooseCommand(State, mover));
        }

        /// <summary>규칙 엔진 기준으로 이 카드를 내려면 대상을 골라야 하는가.</summary>
        public bool RequiresTarget(Entity card) => Engine.RequiresChosenTarget(card);
    }
}
