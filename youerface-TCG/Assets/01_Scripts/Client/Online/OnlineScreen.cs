using System.Collections.Generic;
using TCG.Client.Battle;
using TCG.Client.CardMaker;
using TCG.Client.Common;
using TCG.Client.DeckBuilder;
using TCG.Client.Flow;
using TCG.Core.Data;
using TCG.Core.Net;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TCG.Client.Online
{
    /// <summary>
    /// 온라인 방 화면 (Online 장면): 내 덱·이름·방 규칙(카드 점수 상한, GameDesign §6)을 정하고 방을 만들거나 들어간다.
    /// 인터넷 대전(UGS Relay, M6-4)은 아직 없어 버튼만 꺼 둔다. 대신 "가짜 연결로 시험"은 같은 프로그램 안의 컴퓨터를
    /// 상대(손님 또는 방장)로 붙여 온라인 흐름(핸드셰이크·시야·타이머)을 그대로 거친다. 대전이 시작되면 Battle 장면으로 넘어간다.
    /// </summary>
    public sealed class OnlineScreen : MonoBehaviour
    {
        public const string SceneName = "Online";
        static readonly Color Idle = new Color(0.16f, 0.19f, 0.25f);
        static readonly Color Go = new Color(0.25f, 0.5f, 0.75f);
        static readonly Color Off = new Color(0.14f, 0.15f, 0.18f);

        /// <summary>마지막 방 규칙 (점수 상한). 화면을 다시 열어도 유지.</summary>
        public static int? LastMaxScore { get; set; }
        public static string LastName { get; set; } = "플레이어";

        CardPool _pool;
        readonly List<TitleScreen.DeckOption> _decks = new List<TitleScreen.DeckOption>();
        List<DeckDefinition> _samples;
        TextMeshProUGUI _deckLabel, _deckCheck, _ruleLabel, _status;
        TMP_InputField _name;
        bool _launched;

        public TitleScreen.DeckOption SelectedDeck { get; private set; }
        public RoomRules Room { get; } = new RoomRules();
        public Button HostTestButton { get; private set; }
        public Button GuestTestButton { get; private set; }
        public Button CreateRoomButton { get; private set; }
        public Button JoinRoomButton { get; private set; }
        public string StatusText => _status.text;
        public string DeckCheckText => _deckCheck.text;
        /// <summary>false면 대전이 시작돼도 장면을 넘기지 않는다 (테스트용).</summary>
        public bool AutoLaunch { get; set; } = true;

        void Start()
        {
            Room.MaxCardScore = LastMaxScore;
            UiFactory.EnsureEventSystem();
            var canvas = UiFactory.OverlayCanvas(transform, "Online UI");
            var root = canvas.transform;
            UiFactory.Stretch(root, "Background").gameObject.AddComponent<Image>().color = new Color(0.05f, 0.06f, 0.08f);

            var title = UiFactory.Text(root, "Title", "온라인 대전", new Vector2(0.5f, 1f), new Vector2(0, -110), new Vector2(1400, 120), 80);
            title.fontStyle = FontStyles.Bold;

            var top = new Vector2(0.5f, 1f);
            UiFactory.Text(root, "NameLabel", "이름", top, new Vector2(-330, -250), new Vector2(160, 56), 30);
            _name = UiFactory.InputField(root, "Name", top, new Vector2(60, -250), new Vector2(560, 60), 28);
            _name.text = LastName;
            _name.onEndEdit.AddListener(v => LastName = string.IsNullOrWhiteSpace(v) ? "플레이어" : v.Trim());

            UiFactory.Button(root, "DeckPrev", "◀", top, new Vector2(-420, -340), new Vector2(64, 56), Idle, () => StepDeck(-1), 28);
            _deckLabel = UiFactory.Text(root, "Deck", "", top, new Vector2(0, -340), new Vector2(720, 56), 30);
            UiFactory.Button(root, "DeckNext", "▶", top, new Vector2(420, -340), new Vector2(64, 56), Idle, () => StepDeck(1), 28);
            _deckCheck = UiFactory.Text(root, "DeckCheck", "", top, new Vector2(0, -392), new Vector2(1500, 40), 22);

            UiFactory.Text(root, "RuleTitle", "방 규칙 (방장이 정함)", top, new Vector2(0, -460), new Vector2(800, 50), 30);
            UiFactory.Button(root, "ScoreDown", "−", top, new Vector2(-330, -520), new Vector2(64, 56), Idle, () => StepScore(-1), 32);
            _ruleLabel = UiFactory.Text(root, "Rule", "", top, new Vector2(0, -520), new Vector2(520, 56), 30);
            UiFactory.Button(root, "ScoreUp", "+", top, new Vector2(330, -520), new Vector2(64, 56), Idle, () => StepScore(1), 32);
            UiFactory.Button(root, "ScoreOff", "제한 없음", top, new Vector2(520, -520), new Vector2(200, 56), Idle, () => SetMaxScore(null), 26);

            UiFactory.Text(root, "NetTitle", "인터넷 대전 (Unity Relay)", top, new Vector2(-380, -620), new Vector2(640, 50), 30);
            CreateRoomButton = UiFactory.Button(root, "CreateRoom", "방 만들기", top, new Vector2(-540, -700), new Vector2(300, 80), Off, () => { }, 30);
            JoinRoomButton = UiFactory.Button(root, "JoinRoom", "참가 코드로 들어가기", top, new Vector2(-220, -700), new Vector2(300, 80), Off, () => { }, 26);
            CreateRoomButton.interactable = false;
            JoinRoomButton.interactable = false;
            UiFactory.Text(root, "NetNote", "UGS(Relay·Lobby)를 프로젝트에 연결한 뒤 쓸 수 있습니다", top, new Vector2(-380, -770), new Vector2(700, 40), 22)
                .color = new Color(0.7f, 0.72f, 0.78f);

            UiFactory.Text(root, "TestTitle", "가짜 연결로 시험 (컴퓨터 상대)", top, new Vector2(380, -620), new Vector2(640, 50), 30);
            HostTestButton = UiFactory.Button(root, "HostTest", "내가 방장", top, new Vector2(220, -700), new Vector2(300, 80), Go, StartHostTest, 30);
            GuestTestButton = UiFactory.Button(root, "GuestTest", "내가 손님", top, new Vector2(540, -700), new Vector2(300, 80), Go, StartGuestTest, 30);
            UiFactory.Text(root, "TestNote", "같은 PC 안에서 온라인과 같은 순서로 주고받습니다", top, new Vector2(380, -770), new Vector2(700, 40), 22)
                .color = new Color(0.7f, 0.72f, 0.78f);

            _status = UiFactory.Text(root, "Status", "", new Vector2(0.5f, 0f), new Vector2(0, 200), new Vector2(1600, 90), 26);
            _status.color = new Color(1f, 0.85f, 0.45f);

            UiFactory.Button(root, "Back", "돌아가기", new Vector2(0f, 0f), new Vector2(200, 80), new Vector2(300, 80),
                new Color(0.25f, 0.32f, 0.45f), Back, 32f);

            ReloadDecks(new LocalCardLibrary(), new LocalDeckLibrary());
            RefreshRule();
        }

        /// <summary>덱 선택지: 샘플 덱 + 덱 규칙을 통과한 내 덱 (방 규칙은 따로 표시).</summary>
        public void ReloadDecks(LocalCardLibrary cards, LocalDeckLibrary decks)
        {
            _pool = CardPool.Load(cards);
            _samples = SampleData.LoadDecks();
            _decks.Clear();
            var rules = RuleConfig.CreateDefault();
            foreach (var sample in _samples)
                _decks.Add(new TitleScreen.DeckOption { Key = TitleScreen.SamplePrefix + sample.Id, Label = $"샘플 덱 · {sample.Name}", Deck = sample, IsSample = true });
            // 불법 덱도 고를 수 있게 넣고 표시한다 (방 규칙 검사 줄에 이유가 나오고, 방장은 방을 만들 수 없다).
            foreach (var entry in decks.List())
                _decks.Add(TitleScreen.MyDeckOption(entry.Id, entry.Deck, _pool.Catalog, rules));
            SelectDeck(BattleLaunch.DeckKey);
        }

        public void SelectDeck(string key)
        {
            SelectedDeck = _decks.Find(d => d.Key == key) ?? (_decks.Count > 0 ? _decks[0] : null);
            BattleLaunch.DeckKey = SelectedDeck?.Key;
            _deckLabel.text = SelectedDeck != null ? "덱: " + SelectedDeck.Label : "덱 없음";
            RefreshDeckCheck();
        }

        public void StepDeck(int delta)
        {
            if (_decks.Count == 0)
                return;
            int index = Mathf.Max(0, _decks.IndexOf(SelectedDeck));
            SelectDeck(_decks[(index + delta + _decks.Count) % _decks.Count].Key);
        }

        public void SetMaxScore(int? max)
        {
            Room.MaxCardScore = max;
            LastMaxScore = max;
            RefreshRule();
        }

        void StepScore(int delta)
        {
            // 처음 켤 때는 지금 덱의 최고 점수에서 시작한다.
            if (!Room.MaxCardScore.HasValue)
            {
                var report = SelectedDeck != null ? DeckValidator.Check(SelectedDeck.Deck, _pool.Catalog, RuleConfig.CreateDefault()) : null;
                SetMaxScore(report?.MaxScore ?? 0);
                return;
            }
            SetMaxScore(Room.MaxCardScore.Value + delta);
        }

        void RefreshRule()
        {
            _ruleLabel.text = Room.Describe();
            RefreshDeckCheck();
        }

        void RefreshDeckCheck()
        {
            if (_deckCheck == null || SelectedDeck == null || _pool == null)
                return;
            var report = Room.Check(SelectedDeck.Deck, _pool.Catalog, RuleConfig.CreateDefault());
            _deckCheck.text = report.IsValid
                ? $"방 규칙에 맞습니다 (카드 최고 점수 {report.MaxScore})"
                : $"방 규칙에 걸림: {string.Join(" / ", report.Messages())}";
            _deckCheck.color = report.IsValid ? new Color(0.6f, 0.9f, 0.65f) : new Color(1f, 0.55f, 0.45f);
        }

        /// <summary>컴퓨터 상대 덱: 내 덱과 ID가 다른 첫 샘플 덱.</summary>
        DeckDefinition ComputerDeck()
        {
            foreach (var sample in _samples)
            {
                if (SelectedDeck == null || sample.Id != SelectedDeck.Deck.Id)
                    return sample;
            }
            return _samples.Count > 0 ? _samples[0] : null;
        }

        public void StartHostTest()
        {
            if (!CanStart(asHost: true))
                return;
            var me = new HostSetup
            {
                AppVersion = Application.version,
                HostName = LastName,
                Room = Copy(Room),
                Deck = SelectedDeck.Deck,
                Cards = _pool.Catalog,
                Seed = NewSeed(),
            };
            var computer = new GuestSetup { AppVersion = Application.version, Name = "컴퓨터", Deck = ComputerDeck(), Cards = _pool.Catalog };
            OnlineRunner.StartLoopbackAsHost(me, computer);
        }

        public void StartGuestTest()
        {
            if (!CanStart(asHost: false))
                return;
            var computer = new HostSetup
            {
                AppVersion = Application.version,
                HostName = "컴퓨터",
                Room = Copy(Room),
                Deck = ComputerDeck(),
                Cards = _pool.Catalog,
                Seed = NewSeed(),
            };
            var me = new GuestSetup { AppVersion = Application.version, Name = LastName, Deck = SelectedDeck.Deck, Cards = _pool.Catalog };
            OnlineRunner.StartLoopbackAsGuest(computer, me);
        }

        bool CanStart(bool asHost)
        {
            if (SelectedDeck == null || ComputerDeck() == null)
            {
                _status.text = "덱이 없습니다.";
                return false;
            }
            // 방장 덱은 방 규칙을 통과해야 방을 만들 수 있다. 손님 덱은 방장이 검사한다(걸리면 이유를 보여 줌).
            var computerRoomCheck = Room.Check(ComputerDeck(), _pool.Catalog, RuleConfig.CreateDefault());
            var mine = Room.Check(SelectedDeck.Deck, _pool.Catalog, RuleConfig.CreateDefault());
            if (asHost && !mine.IsValid)
            {
                _status.text = "내 덱이 방 규칙에 맞지 않아 방을 만들 수 없습니다.";
                return false;
            }
            if (!asHost && !computerRoomCheck.IsValid)
            {
                _status.text = "컴퓨터(방장) 덱이 이 방 규칙에 맞지 않습니다. 점수 상한을 올려 주세요.";
                return false;
            }
            _launched = false;
            return true;
        }

        static RoomRules Copy(RoomRules room) => new RoomRules { MaxCardScore = room.MaxCardScore };

        static int NewSeed() => unchecked((int)System.DateTime.Now.Ticks) & int.MaxValue;

        void Update()
        {
            var runner = OnlineRunner.Current;
            if (runner == null || _status == null)
                return;
            _status.text = runner.Status;
            if (runner.InMatch && AutoLaunch && !_launched)
            {
                _launched = true;
                SceneManager.LoadScene(BattleLaunch.BattleScene);
            }
        }

        void Back()
        {
            OnlineRunner.Current?.Close();
            BattleLaunch.ToTitle();
        }
    }
}
