using System.Collections.Generic;
using TCG.Client.Common;
using TCG.Core.AI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TCG.Client.Flow
{
    /// <summary>
    /// 시작 화면 (Title 장면): 싱글 배틀 난이도 4개 중 하나를 고르고 "대전 시작".
    /// 덱은 지금 양쪽 다 샘플 덱 고정 (덱 선택은 나중에).
    /// </summary>
    public sealed class TitleScreen : MonoBehaviour
    {
        static readonly AiDifficulty[] Order = { AiDifficulty.Easy, AiDifficulty.Normal, AiDifficulty.Hard, AiDifficulty.Challenge };
        static readonly Color Idle = new Color(0.16f, 0.19f, 0.25f);
        static readonly Color Picked = new Color(0.25f, 0.5f, 0.75f);

        readonly Dictionary<AiDifficulty, Button> _options = new Dictionary<AiDifficulty, Button>();

        public AiDifficulty Selected { get; private set; }
        public Button StartButton { get; private set; }
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

            UiFactory.Text(root, "Deck", "덱: 샘플 덱 (양쪽 고정)", new Vector2(0.5f, 0f), new Vector2(0, 300), new Vector2(1000, 50), 26)
                .color = new Color(0.7f, 0.72f, 0.78f);
            StartButton = UiFactory.Button(root, "Start", "대전 시작", new Vector2(0.5f, 0f), new Vector2(0, 190), new Vector2(360, 100),
                new Color(0.75f, 0.55f, 0.2f), StartBattle, 40f);

            Select(BattleLaunch.Difficulty);
        }

        public void Select(AiDifficulty difficulty)
        {
            Selected = difficulty;
            foreach (var pair in _options)
                pair.Value.image.color = pair.Key == difficulty ? Picked : Idle;
        }

        public void StartBattle() => BattleLaunch.StartBattle(Selected);

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
