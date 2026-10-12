using TCG.Client.Board;
using TCG.Client.Common;
using TCG.Client.Interaction;
using TCG.Client.Presentation;
using TCG.Core.AI;
using TCG.Core.Commands;
using TCG.Core.Data;
using TCG.Core.Rules;
using TCG.Core.State;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace TCG.Client.Battle
{
    /// <summary>
    /// Battle 장면 시작점. 샘플 덱으로 대전을 만들고 보드·연출·입력을 붙인다.
    /// 기본은 사람(아래) vs 컴퓨터(위). 관전 모드면 양쪽 다 컴퓨터가 둔다.
    /// <see cref="Build"/>는 Play가 아닐 때(에디터 화면 캡처)도 호출할 수 있다 (그때는 상태를 연출 없이 그림).
    /// </summary>
    public sealed class BattleBootstrap : MonoBehaviour
    {
        [SerializeField] int _seed = 2026;
        [Tooltip("시작 전에 연출 없이 미리 둘 컴퓨터 행동 수 (화면 캡처·확인용). 0이면 처음부터.")]
        [SerializeField] int _previewSteps;
        [SerializeField] Camera _camera;

        [Header("대전")]
        [Tooltip("켜면 아래쪽도 컴퓨터가 둔다 (관전 모드).")]
        [SerializeField] bool _spectate;
        [Tooltip("관전 모드에서 아래쪽 컴퓨터 난이도.")]
        [SerializeField] AiDifficulty _bottomAi = AiDifficulty.Normal;
        [Tooltip("상대(위쪽) 컴퓨터 난이도.")]
        [SerializeField] AiDifficulty _topAi = AiDifficulty.Normal;
        [Tooltip("연출 배속.")]
        [SerializeField] float _speed = 1f;

        Transform _root;

        public const int Viewer = 0;
        public BattleSession Session { get; private set; }
        public GameState State => Session?.State;
        public BoardView Board { get; private set; }
        public PresentationPlayer Player { get; private set; }
        public DecisionPanel Decisions { get; private set; }
        public int CommandsApplied => Session?.CommandsApplied ?? 0;

        public bool Spectate
        {
            get => Session?.Spectate ?? _spectate;
            set
            {
                _spectate = value;
                if (Session != null)
                    Session.Spectate = value;
            }
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

        public void Configure(int seed, int previewSteps)
        {
            _seed = seed;
            _previewSteps = previewSteps;
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

            var engine = new GameEngine();
            var ai = new[] { AiPlayers.Create(_bottomAi, _seed, engine), AiPlayers.Create(_topAi, _seed + 1, engine) };
            var state = new GameState(RuleConfig.CreateDefault(), _seed);
            engine.StartGame(state, catalog.Resolve(decks[0]), catalog.Resolve(decks[1 % decks.Count]));
            for (int i = 0; i < _previewSteps && state.Phase == GamePhase.InProgress; i++)
            {
                int mover = state.Decision?.PlayerIndex ?? state.ActivePlayerIndex;
                engine.Apply(state, ai[mover].ChooseCommand(state, mover));
            }

            Board = BoardView.Create(_root, camera, Viewer);
            Board.Render(state);
            Player = _root.gameObject.AddComponent<PresentationPlayer>();
            Player.Init(Board);
            PresentationTime.Speed = _speed;

            Session = new BattleSession(state, engine, Player, Viewer, ai) { Spectate = _spectate };

            if (!Application.isPlaying)
                return;

            EnsureEventSystem();
            Board.Hud.CreateEndTurnButton(EndTurn);
            _root.gameObject.AddComponent<BattleInput>().Init(Session, Board);
            Decisions = _root.gameObject.AddComponent<DecisionPanel>();
            Decisions.Init(Session, Board.Hud.transform);
        }

        void Update()
        {
            if (Session == null)
                return;
            Session.Tick(PresentationTime.DeltaTime);

            var endTurn = Board.Hud.EndTurnButton;
            if (endTurn != null)
                endTurn.interactable = Session.HumanCanAct && State.Decision == null;
        }

        void EndTurn()
        {
            if (!Session.TrySubmit(new EndTurnCommand(Viewer), out var reason))
                Board.Hud.ShowHint(reason);
        }

        static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
                return;
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<InputSystemUIInputModule>();
        }
    }
}
