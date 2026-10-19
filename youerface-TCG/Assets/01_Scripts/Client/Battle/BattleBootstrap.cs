using System.Collections.Generic;
using TCG.Client.Board;
using TCG.Client.Common;
using TCG.Client.Flow;
using TCG.Client.Interaction;
using TCG.Client.Online;
using TCG.Client.Presentation;
using TCG.Client.UserAssets;
using TCG.Core.AI;
using TCG.Core.Commands;
using TCG.Core.Data;
using TCG.Core.Effects;
using TCG.Core.Events;
using TCG.Core.Net;
using TCG.Core.Rules;
using TCG.Core.State;
using UnityEngine;

namespace TCG.Client.Battle
{
    /// <summary>
    /// Battle 장면 시작점. 시작 화면에서 고른 덱(없으면 샘플 덱 두 개)으로 대전을 만들고 보드·연출·입력을 붙인다.
    /// 기본은 사람(아래) vs 컴퓨터(위). 관전 모드면 양쪽 다 컴퓨터가 둔다.
    /// 시작 화면에서 왔으면(<see cref="BattleLaunch"/>) 고른 난이도·시드로 시작하고, 끝나면 결과 화면(<see cref="ResultPanel"/>)을 띄운다.
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
        BattleDecks _decks;
        OnlineRunner _online;
        int _viewer = Viewer;
        UnityEngine.UI.Button _concede;
        bool _concedeArmed;

        public const int Viewer = 0;
        /// <summary>온라인 대전인가 (방 화면에서 왔음).</summary>
        public bool IsOnline => _online != null;
        /// <summary>화면 아래쪽 플레이어 (온라인 손님이면 1).</summary>
        public int ViewerSeat => _viewer;
        public BattleSession Session { get; private set; }
        public GameState State => Session?.State;
        public BoardView Board { get; private set; }
        public PresentationPlayer Player { get; private set; }
        public DecisionPanel Decisions { get; private set; }
        /// <summary>대전이 끝나고 연출이 다 끝나면 생긴다.</summary>
        public ResultPanel Result { get; private set; }
        public AiDifficulty OpponentDifficulty => _topAi;
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
            if (OnlineRunner.Current != null && OnlineRunner.Current.Link != null)
            {
                _online = OnlineRunner.Current;
                TryBuildOnline();
                return;
            }
            if (BattleLaunch.TryConsume(out var difficulty, out int seed, out var decks))
            {
                _topAi = difficulty;
                _seed = seed;
                _decks = decks;
            }
            Build();
        }

        public void Build()
        {
            if (_root != null)
                Shapes.DestroyImmediateOrDeferred(_root.gameObject);
            _root = new GameObject("Battle (generated)").transform;
            _root.SetParent(transform, false);
            Result = null;

            var camera = _camera != null ? _camera : Camera.main;
            List<CardDefinition> deck0, deck1;
            if (_decks != null)
            {
                deck0 = _decks.Player;
                deck1 = _decks.Opponent;
            }
            else
            {
                var catalog = SampleData.LoadCatalog();
                var decks = SampleData.LoadDecks();
                deck0 = catalog.Resolve(decks[0]);
                deck1 = catalog.Resolve(decks[1 % decks.Count]);
            }

            var engine = new GameEngine();
            var ai = new[] { AiPlayers.Create(_bottomAi, _seed, engine), AiPlayers.Create(_topAi, _seed + 1, engine) };
            var state = new GameState(RuleConfig.CreateDefault(), _seed);
            var startEvents = engine.StartGame(state, deck0, deck1);
            // 미리 진행(화면 캡처용): 행동 기록·체인 표시에 넣을 수 있게 단계별 이벤트를 남긴다.
            var previewHistory = new List<(IReadOnlyList<GameEvent> events, GameState before, GameState after)>();
            for (int i = 0; i < _previewSteps && state.Phase == GamePhase.InProgress; i++)
            {
                int mover = state.Decision?.PlayerIndex ?? state.ActivePlayerIndex;
                var stepBefore = state.Clone();
                var stepEvents = engine.Apply(state, ai[mover].ChooseCommand(state, mover));
                previewHistory.Add((stepEvents, stepBefore, state.Clone()));
            }

            Board = BoardView.Create(_root, camera, Viewer);
            Board.Sounds = _root.gameObject.AddComponent<CardSoundPlayer>();
            Board.Sounds.Preload(deck0);
            Board.Sounds.Preload(deck1);
            Board.Render(state);
            Player = _root.gameObject.AddComponent<PresentationPlayer>();
            Player.Init(Board);
            PresentationTime.Speed = _speed;
            foreach (var (events, before, after) in previewHistory)
            {
                var context = new PresentationContext(Board, before, after, EffectRegistry.Default, this);
                foreach (var e in events)
                {
                    Board.Hud.Log.Record(context, e);
                    Board.Hud.Chain.Record(context, e);
                }
            }

            Session = new BattleSession(state, engine, Player, Viewer, ai) { Spectate = _spectate };

            if (!Application.isPlaying)
                return;
            if (_previewSteps == 0)
                PlayCoinToss(startEvents, state);

            UiFactory.EnsureEventSystem();
            Board.Hud.CreateEndTurnButton(EndTurn);
            var input = _root.gameObject.AddComponent<BattleInput>();
            input.Init(Session, Board);
            input.Availability = _root.gameObject.AddComponent<AvailabilityGlow>();
            input.Availability.Init(Session, input, Board);
            Decisions = _root.gameObject.AddComponent<DecisionPanel>();
            Decisions.Init(Session, Board.Hud.transform);
        }

        /// <summary>
        /// 온라인 대전 화면: 첫 변화(대전 시작 직후의 시야 상태)가 오면 그 상태로 보드를 그린다. 아래쪽 = 내 자리.
        /// </summary>
        void TryBuildOnline()
        {
            var link = _online.Link;
            if (!link.TryDequeue(out var first))
                return;

            if (_root != null)
                Shapes.DestroyImmediateOrDeferred(_root.gameObject);
            _root = new GameObject("Battle (online)").transform;
            _root.SetParent(transform, false);
            Result = null;
            _viewer = link.Seat;

            var camera = _camera != null ? _camera : Camera.main;
            Board = BoardView.Create(_root, camera, _viewer);
            Board.Sounds = _root.gameObject.AddComponent<CardSoundPlayer>();
            Board.Sounds.Preload(new List<CardDefinition>(link.MatchCards));
            Board.Render(first.After);
            Player = _root.gameObject.AddComponent<PresentationPlayer>();
            Player.Init(Board);
            PresentationTime.Speed = _speed;

            PlayCoinToss(first.Events, first.After);
            Session = new BattleSession(link, first.After, new GameEngine(), Player);
            Session.OnlineUpdated += OnOnlineUpdate;
            Session.OnlineRejected += reason => Board.Hud.ShowHint(reason);

            UiFactory.EnsureEventSystem();
            Board.Hud.CreateEndTurnButton(EndTurn);
            _concede = UiFactory.Button(Board.Hud.transform, "Concede", "항복", new Vector2(1f, 1f), new Vector2(-110, -50),
                new Vector2(160, 64), new Color(0.35f, 0.22f, 0.22f), Concede, 26f);
            var input = _root.gameObject.AddComponent<BattleInput>();
            input.Init(Session, Board);
            input.Availability = _root.gameObject.AddComponent<AvailabilityGlow>();
            input.Availability.Init(Session, input, Board);
            Decisions = _root.gameObject.AddComponent<DecisionPanel>();
            Decisions.Init(Session, Board.Hud.transform);
        }

        /// <summary>
        /// 대전 시작 코인 토스 (GameDesign §11.3). 보드는 이미 시작 상태로 그려져 있으므로 상태 변화 없는 묶음으로 재생한다.
        /// 재생 중에는 입력·컴퓨터 차례가 기다린다(<see cref="PresentationPlayer.IsPlaying"/>).
        /// </summary>
        void PlayCoinToss(IReadOnlyList<GameEvent> startEvents, GameState state)
        {
            if (startEvents == null)
                return;
            for (int i = 0; i < startEvents.Count; i++)
            {
                if (startEvents[i] is GameStartedEvent started)
                {
                    Board.Hud.Log.Defer(startEvents);
                    Player.Enqueue(new GameEvent[] { started }, state.Clone(), state.Clone());
                    return;
                }
            }
        }

        void OnOnlineUpdate(SeatUpdate update)
        {
            if (update.Auto == AutoReason.None || update.AutoPlayer < 0)
                return;
            bool me = update.AutoPlayer == _viewer;
            string who = me ? "내" : "상대";
            Board.Hud.ShowHint(update.Auto == AutoReason.Timeout
                ? $"{who} 시간 초과 — 자동으로 진행했습니다"
                : $"{who} 연결이 끊겨 자동으로 진행했습니다", 2.5f);
        }

        /// <summary>항복: 처음 누르면 확인 문구, 한 번 더 누르면 낸다.</summary>
        void Concede()
        {
            if (!_concedeArmed)
            {
                _concedeArmed = true;
                UiFactory.SetLabel(_concede, "정말 항복?");
                return;
            }
            if (!_online.Link.Submit(new ConcedeCommand(_viewer), out var reason))
                Board.Hud.ShowHint(reason);
            _concede.interactable = false;
        }

        void Update()
        {
            if (_online != null && Session == null)
                TryBuildOnline();
            if (Session == null)
                return;
            Session.Tick(PresentationTime.DeltaTime);

            var endTurn = Board.Hud.EndTurnButton;
            if (endTurn != null)
                endTurn.interactable = Session.HumanCanAct && State.Decision == null;
            Board.Hud.SetThinking(Session.AiThinking && !Session.Spectate);

            if (_online != null)
            {
                UpdateOnlineHud();
                bool over = !Session.InProgress || _online.Link.Ended;
                if (Result == null && over && !Player.IsPlaying)
                {
                    Result = ResultPanel.CreateOnline(Board.Hud.transform, State, _viewer, _online.Link.EndReason, ExitOnline);
                    if (_concede != null)
                        _concede.gameObject.SetActive(false);
                }
                return;
            }

            if (Result == null && !Session.InProgress && !Player.IsPlaying)
                Result = ResultPanel.Create(Board.Hud.transform, State, Viewer, _topAi);
        }

        /// <summary>남은 시간(내·상대 차례), 시간 초과 횟수, 연결 문제를 한 줄로.</summary>
        void UpdateOnlineHud()
        {
            var link = _online.Link;
            string note = link.ConnectionNote;
            var clock = link.Clock;
            if (note != null)
            {
                Board.Hud.SetOnline(note, new Color(1f, 0.55f, 0.45f));
                return;
            }
            if (clock == null || clock.Mover < 0 || !Session.InProgress)
            {
                Board.Hud.SetOnline("", Color.white);
                return;
            }
            bool mine = clock.Mover == _viewer;
            int seconds = HostLink.Seconds(clock.RemainingMs);
            string text = $"{(clock.Deciding ? "선택" : mine ? "내 턴" : "상대 턴")} {seconds / 60}:{seconds % 60:00}";
            int timeouts = clock.Timeouts != null && clock.Mover < clock.Timeouts.Length ? clock.Timeouts[clock.Mover] : 0;
            if (timeouts > 0)
                text += $"   시간 초과 {timeouts}/{State.Rules.MaxConsecutiveTimeouts}";
            var color = mine && seconds <= 15 ? new Color(1f, 0.5f, 0.4f) : new Color(0.85f, 0.9f, 1f);
            Board.Hud.SetOnline(text, color);
        }

        void ExitOnline()
        {
            _online?.Close();
            _online = null;
            BattleLaunch.ToTitle();
        }

        void EndTurn()
        {
            if (!Session.TrySubmit(new EndTurnCommand(_viewer), out var reason))
                Board.Hud.ShowHint(reason);
        }
    }
}
