using TCG.Client.Board;
using TCG.Client.Common;
using TCG.Core.AI;
using TCG.Core.Data;
using TCG.Core.Rules;
using TCG.Core.State;
using UnityEngine;

namespace TCG.Client.Battle
{
    /// <summary>
    /// Battle 장면 시작점. 샘플 덱으로 대전을 만들고 보드를 그린다.
    /// M3 1단계: 아직 조작이 없으므로, 보드가 채워진 모습을 보려고 컴퓨터끼리 몇 수를 미리 둔 상태를 보여 준다.
    /// <see cref="Build"/>는 Play가 아닐 때(에디터 화면 캡처)도 호출할 수 있다.
    /// </summary>
    public sealed class BattleBootstrap : MonoBehaviour
    {
        [SerializeField] int _seed = 2026;
        [Tooltip("보드를 채우려고 미리 둘 컴퓨터 행동 수 (1단계 미리보기용).")]
        [SerializeField] int _previewSteps = 45;
        [SerializeField] Camera _camera;

        Transform _root;

        public GameState State { get; private set; }
        public BoardView Board { get; private set; }
        public const int Viewer = 0;

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
            State = new GameState(RuleConfig.CreateDefault(), _seed);
            engine.StartGame(State, catalog.Resolve(decks[0]), catalog.Resolve(decks[1 % decks.Count]));
            RunPreview(engine);

            Board = BoardView.Create(_root, camera);
            Board.Render(State, Viewer);
        }

        void RunPreview(GameEngine engine)
        {
            var ai = new[] { AiPlayers.Create(AiDifficulty.Normal, _seed, engine), AiPlayers.Create(AiDifficulty.Normal, _seed + 1, engine) };
            for (int i = 0; i < _previewSteps && State.Phase == GamePhase.InProgress; i++)
            {
                int mover = State.Decision?.PlayerIndex ?? State.ActivePlayerIndex;
                engine.Apply(State, ai[mover].ChooseCommand(State, mover));
            }
        }
    }
}
