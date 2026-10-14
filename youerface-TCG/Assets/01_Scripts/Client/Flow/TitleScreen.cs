using System.Collections.Generic;
using TCG.Client.Battle;
using TCG.Client.CardMaker;
using TCG.Client.Common;
using TCG.Client.DeckBuilder;
using TCG.Core.AI;
using TCG.Core.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TCG.Client.Flow
{
    /// <summary>
    /// 시작 화면 (Title 장면): 싱글 배틀 난이도 4개 중 하나와 내 덱을 고르고 "대전 시작".
    /// 덱 선택지 = 샘플 덱 + 규칙 검사를 통과한 내 덱(<see cref="LocalDeckLibrary"/>). 상대는 샘플 덱(내 덱과 다른 첫 샘플 덱).
    /// </summary>
    public sealed class TitleScreen : MonoBehaviour
    {
        static readonly AiDifficulty[] Order = { AiDifficulty.Easy, AiDifficulty.Normal, AiDifficulty.Hard, AiDifficulty.Challenge };
        static readonly Color Idle = new Color(0.16f, 0.19f, 0.25f);
        static readonly Color Picked = new Color(0.25f, 0.5f, 0.75f);

        public const string SamplePrefix = "sample:";

        /// <summary>덱 선택지 하나. Key: 샘플은 "sample:덱ID", 내 덱은 덱 ID.</summary>
        public sealed class DeckOption
        {
            public string Key;
            public string Label;
            public DeckDefinition Deck;
            public bool IsSample;
        }

        readonly Dictionary<AiDifficulty, Button> _options = new Dictionary<AiDifficulty, Button>();
        readonly List<DeckOption> _decks = new List<DeckOption>();
        List<DeckDefinition> _samples;
        CardPool _pool;
        TextMeshProUGUI _deckLabel, _deckNote;

        public AiDifficulty Selected { get; private set; }
        public Button StartButton { get; private set; }
        public Button CardMakerButton { get; private set; }
        public Button DeckBuilderButton { get; private set; }
        public Button OnlineButton { get; private set; }
        public IReadOnlyList<DeckOption> DeckOptions => _decks;
        public DeckOption SelectedDeck { get; private set; }
        /// <summary>규칙 검사에 걸려 선택지에서 뺀 내 덱 수.</summary>
        public int SkippedDecks { get; private set; }
        public Button OptionButton(AiDifficulty difficulty) => _options[difficulty];

        void Start()
        {
            UiFactory.EnsureEventSystem();
            var canvas = UiFactory.OverlayCanvas(transform, "Title UI");
            var root = canvas.transform;
            UiFactory.Stretch(root, "Background").gameObject.AddComponent<Image>().color = new Color(0.05f, 0.06f, 0.08f);

            var title = UiFactory.Text(root, "Title", "youerface TCG", new Vector2(0.5f, 1f), new Vector2(0, -170), new Vector2(1400, 140), 96);
            title.fontStyle = FontStyles.Bold;
            UiFactory.Text(root, "Mode", "싱글 배틀 — 컴퓨터 난이도를 고르세요", new Vector2(0.5f, 1f), new Vector2(0, -290), new Vector2(1400, 60), 36);

            for (int i = 0; i < Order.Length; i++)
            {
                var difficulty = Order[i];
                var button = UiFactory.Button(root, $"Difficulty_{difficulty}", "", new Vector2(0.5f, 0.5f),
                    new Vector2((i - 1.5f) * 380, 20), new Vector2(340, 240), Idle, () => Select(difficulty));
                Destroy(button.GetComponentInChildren<TextMeshProUGUI>().gameObject);
                var name = UiFactory.Text(button.transform, "Name", KoreanTerms.Of(difficulty), new Vector2(0.5f, 1f), new Vector2(0, -50), new Vector2(320, 60), 42);
                name.fontStyle = FontStyles.Bold;
                var desc = UiFactory.Text(button.transform, "Desc", Describe(difficulty), new Vector2(0.5f, 0.5f), new Vector2(0, -35), new Vector2(290, 130), 24);
                desc.color = new Color(0.85f, 0.88f, 0.95f);
                _options[difficulty] = button;
            }

            UiFactory.Button(root, "DeckPrev", "◀", new Vector2(0.5f, 0f), new Vector2(-380, 320), new Vector2(64, 56), Idle, () => StepDeck(-1), 28);
            _deckLabel = UiFactory.Text(root, "Deck", "", new Vector2(0.5f, 0f), new Vector2(0, 320), new Vector2(680, 56), 30);
            UiFactory.Button(root, "DeckNext", "▶", new Vector2(0.5f, 0f), new Vector2(380, 320), new Vector2(64, 56), Idle, () => StepDeck(1), 28);
            _deckNote = UiFactory.Text(root, "DeckNote", "", new Vector2(0.5f, 0f), new Vector2(0, 268), new Vector2(1200, 36), 22);
            _deckNote.color = new Color(0.7f, 0.72f, 0.78f);
            StartButton = UiFactory.Button(root, "Start", "대전 시작", new Vector2(0.5f, 0f), new Vector2(0, 190), new Vector2(360, 100),
                new Color(0.75f, 0.55f, 0.2f), StartBattle, 40f);

            CardMakerButton = UiFactory.Button(root, "CardMaker", "카드 제작", new Vector2(1f, 0f), new Vector2(-200, 80), new Vector2(300, 80),
                new Color(0.3f, 0.45f, 0.55f), () => UnityEngine.SceneManagement.SceneManager.LoadScene("CardMaker"), 32f);
            OnlineButton = UiFactory.Button(root, "Online", "온라인 대전", new Vector2(1f, 0f), new Vector2(-840, 80), new Vector2(300, 80),
                new Color(0.3f, 0.5f, 0.4f), () => UnityEngine.SceneManagement.SceneManager.LoadScene(TCG.Client.Online.OnlineScreen.SceneName), 32f);
            DeckBuilderButton = UiFactory.Button(root, "DeckBuilder", "덱 편집", new Vector2(1f, 0f), new Vector2(-520, 80), new Vector2(300, 80),
                new Color(0.3f, 0.45f, 0.55f), () => UnityEngine.SceneManagement.SceneManager.LoadScene(DeckBuilderScreen.SceneName), 32f);

            Select(BattleLaunch.Difficulty);
            ReloadDecks(new LocalCardLibrary(), new LocalDeckLibrary());
        }

        /// <summary>덱 선택지를 다시 만든다 (테스트는 임시 보관함을 넘긴다). 마지막으로 고른 덱을 다시 고른다.</summary>
        public void ReloadDecks(LocalCardLibrary cards, LocalDeckLibrary decks)
        {
            _pool = CardPool.Load(cards);
            _samples = SampleData.LoadDecks();
            _decks.Clear();
            SkippedDecks = 0;
            var rules = RuleConfig.CreateDefault();
            foreach (var sample in _samples)
                _decks.Add(new DeckOption { Key = SamplePrefix + sample.Id, Label = $"샘플 덱 · {sample.Name}", Deck = sample, IsSample = true });
            foreach (var entry in decks.List())
            {
                if (!DeckValidator.Check(entry.Deck, _pool.Catalog, rules).IsValid)
                {
                    SkippedDecks++;
                    continue;
                }
                _decks.Add(new DeckOption { Key = entry.Id, Label = $"내 덱 · {entry.Deck.Name}", Deck = entry.Deck });
            }
            SelectDeck(BattleLaunch.DeckKey);
        }

        /// <summary>키로 덱을 고른다. 없는 키면 첫 선택지(첫 샘플 덱).</summary>
        public void SelectDeck(string key)
        {
            SelectedDeck = _decks.Find(d => d.Key == key) ?? (_decks.Count > 0 ? _decks[0] : null);
            BattleLaunch.DeckKey = SelectedDeck?.Key;
            _deckLabel.text = SelectedDeck != null ? "덱: " + SelectedDeck.Label : "덱 없음";
            var opponent = OpponentDeck();
            string note = opponent != null ? $"상대: 샘플 덱 · {opponent.Name}" : "";
            if (SkippedDecks > 0)
                note += $"   (규칙 검사에 걸린 내 덱 {SkippedDecks}개는 뺐습니다)";
            _deckNote.text = note;
        }

        public void StepDeck(int delta)
        {
            if (_decks.Count == 0)
                return;
            int index = Mathf.Max(0, _decks.IndexOf(SelectedDeck));
            SelectDeck(_decks[(index + delta + _decks.Count) % _decks.Count].Key);
        }

        /// <summary>상대 덱: 내 덱과 ID가 다른 첫 샘플 덱 (샘플이 하나뿐이면 그것).</summary>
        DeckDefinition OpponentDeck()
        {
            if (_samples == null || _samples.Count == 0)
                return null;
            foreach (var sample in _samples)
            {
                if (SelectedDeck == null || sample.Id != SelectedDeck.Deck.Id)
                    return sample;
            }
            return _samples[0];
        }

        public void Select(AiDifficulty difficulty)
        {
            Selected = difficulty;
            foreach (var pair in _options)
                pair.Value.image.color = pair.Key == difficulty ? Picked : Idle;
        }

        public void StartBattle()
        {
            var opponent = OpponentDeck();
            if (SelectedDeck == null || opponent == null)
            {
                BattleLaunch.StartBattle(Selected);
                return;
            }
            BattleLaunch.StartBattle(Selected, null, new BattleDecks
            {
                Player = _pool.Catalog.Resolve(SelectedDeck.Deck),
                Opponent = _pool.Catalog.Resolve(opponent),
                PlayerName = SelectedDeck.Deck.Name,
                OpponentName = opponent.Name,
            });
        }

        static string Describe(AiDifficulty difficulty)
        {
            switch (difficulty)
            {
                case AiDifficulty.Easy: return "낼 수 있는 카드와 공격을 대충 고릅니다.";
                case AiDifficulty.Normal: return "행동마다 결과를 계산해 가장 좋은 수를 둡니다.";
                case AiDifficulty.Hard: return "자기 턴 끝과 내 다음 턴 대응까지 내다봅니다.";
                case AiDifficulty.Challenge: return "더 많이 내다보고, 내 손패를 봅니다.";
                default: return "";
            }
        }
    }
}
