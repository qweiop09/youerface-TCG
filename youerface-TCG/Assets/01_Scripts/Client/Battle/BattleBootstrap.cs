using TCG.Client.Board;
using TCG.Client.Common;
using TCG.Client.Presentation;
using TCG.Core.AI;
using TCG.Core.Data;
using TCG.Core.Rules;
using TCG.Core.State;
using UnityEngine;

namespace TCG.Client.Battle
{
    /// <summary>
    /// Battle 장면 시작점. 샘플 덱으로 대전을 만들고 보드를 그린다.
    /// M3 2단계: 아직 조작이 없으므로 <b>관전 모드</b> — 컴퓨터 둘이 번갈아 두고, 한 수마다 연출이 끝나면 다음 수를 둔다.
    /// <see cref="Build"/>는 Play가 아닐 때(에디터 화면 캡처)도 호출할 수 있다 (그때는 미리 둔 상태를 연출 없이 그림).
    /// </summary>
    public sealed class BattleBootstrap : MonoBehaviour
    {
        [SerializeField] int _seed = 2026;
        [Tooltip("시작 전에 연출 없이 미리 둘 컴퓨터 행동 수 (화면 캡처·확인용). 0이면 처음부터.")]
        [SerializeField] int _previewSteps;
        [SerializeField] Camera _camera;

        [Header("관전 모드")]
        [SerializeField] bool _spectate = true;
        [SerializeField] AiDifficulty _bottomAi = AiDifficulty.Normal;
        [SerializeField] AiDifficulty _topAi = AiDifficulty.Hard;
        [Tooltip("연출 배속.")]
        [SerializeField] float _speed = 1f;
        [Tooltip("컴퓨터가 한 수를 두기 전 기다리는 시간(초, 배속 적용).")]
        [SerializeField] float _thinkDelay = 0.35f;

        Transform _root;
        GameEngine _engine;
        IAiPlayer[] _ai;
        float _waited;

        public const int Viewer = 0;
        public GameState State { get; private set; }
        public BoardView Board { get; private set; }
        public PresentationPlayer Player { get; private set; }
        /// <summary>지금까지 적용한 명령 수.</summary>
        public int CommandsApplied { get; private set; }

        public void Configure(int seed, int previewSteps)
        {
            _seed = seed;
            _previewSteps = previewSteps;
        }

        public float Speed
        {
            get => _speed;
            set
            {
                _speed = value;
                PresentationTime.Speed = value;
            }
        }

        void Start()
        {
            Build();
        }

        public void Build()
        {
            if (_root != null)
                Shapes.DestroyImmediateOrDeferred(_root.gameObject);
            _root = new GameObject("Battle (generated)").transform;
            _root.SetParent(transform, false);

            var camera = _camera != null ? _camera : Camera.main;
            var catalog = SampleData.LoadCatalog();
            var decks = SampleData.LoadDecks();

            _engine = new GameEngine();
            _ai = new[] { AiPlayers.Create(_bottomAi, _seed, _engine), AiPlayers.Create(_topAi, _seed + 1, _engine) };
            State = new GameState(RuleConfig.CreateDefault(), _seed);
            _engine.StartGame(State, catalog.Resolve(decks[0]), catalog.Resolve(decks[1 % decks.Count]));
            for (int i = 0; i < _previewSteps && State.Phase == GamePhase.InProgress; i++)
                ApplyAiCommand();

            Board = BoardView.Create(_root, camera, Viewer);
            Board.Render(State);
            Player = _root.gameObject.AddComponent<PresentationPlayer>();
            Player.Init(Board);
            PresentationTime.Speed = _speed;
        }

        void Update()
        {
            if (!_spectate || Player == null || State == null || State.Phase != GamePhase.InProgress || Player.IsPlaying)
                return;

            _waited += PresentationTime.DeltaTime;
            if (_waited < _thinkDelay)
                return;
            _waited = 0f;

            var before = State.Clone();
            var events = ApplyAiCommand();
            Player.Enqueue(events, before, State.Clone());
        }

        System.Collections.Generic.IReadOnlyList<Core.Events.GameEvent> ApplyAiCommand()
        {
            int mover = State.Decision?.PlayerIndex ?? State.ActivePlayerIndex;
            var command = _ai[mover].ChooseCommand(State, mover);
            CommandsApplied++;
            return _engine.Apply(State, command);
        }
    }
}
