using System.Collections;
using TCG.Client.Common;
using TCG.Client.Presentation;
using TCG.Core.State;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TCG.Client.Board
{
    /// <summary>화면 위에 덮는 정보 표시 (턴, 코스트, 손패·덱 장수, 가운데 알림). 카메라 기준 캔버스라 화면 캡처에도 찍힌다.</summary>
    public sealed class HudView : MonoBehaviour
    {
        sealed class Line
        {
            public string Who;
            public int Hand, Deck;
            public override string ToString() => $"{Who}   손패 {Hand}   덱 {Deck}";
        }

        TextMeshProUGUI _turn, _mine, _theirs, _banner, _hint;
        UnityEngine.UI.Button _endTurn;
        TextMeshProUGUI _thinking, _online;
        int _hintVersion;
        readonly Line[] _lines = { new Line { Who = "나" }, new Line { Who = "상대" } };
        int _bannerVersion;

        public static HudView Create(Transform parent, Camera camera)
        {
            var go = new GameObject("HUD");
            go.transform.SetParent(parent, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1f;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            go.AddComponent<GraphicRaycaster>();
            var view = go.AddComponent<HudView>();
            view._turn = Text(go.transform, "Turn", new Vector2(0.5f, 1f), new Vector2(0, -24), 34, TextAlignmentOptions.Top);
            view._theirs = Text(go.transform, "Opponent", new Vector2(0f, 1f), new Vector2(32, -24), 28, TextAlignmentOptions.TopLeft);
            view._mine = Text(go.transform, "Me", new Vector2(0f, 0f), new Vector2(32, 24), 28, TextAlignmentOptions.BottomLeft);
            view._banner = Text(go.transform, "Banner", new Vector2(0.5f, 0.5f), new Vector2(0, 40), 72, TextAlignmentOptions.Center);
            view._banner.rectTransform.sizeDelta = new Vector2(1400, 200);
            view._banner.fontStyle = FontStyles.Bold;
            view._banner.text = "";
            view._hint = Text(go.transform, "Hint", new Vector2(0.5f, 0f), new Vector2(0, 150), 30, TextAlignmentOptions.Bottom);
            view._hint.rectTransform.sizeDelta = new Vector2(1200, 60);
            view._hint.color = new Color(1f, 0.85f, 0.45f);
            view._hint.text = "";
            view._thinking = Text(go.transform, "Thinking", new Vector2(0.5f, 1f), new Vector2(0, -70), 24, TextAlignmentOptions.Top);
            view._thinking.color = new Color(1f, 0.75f, 0.5f, 0.9f);
            view._thinking.text = "";
            view._online = Text(go.transform, "Online", new Vector2(0.5f, 1f), new Vector2(0, -66), 26, TextAlignmentOptions.Top);
            view._online.rectTransform.sizeDelta = new Vector2(1200, 80);
            view._online.text = "";
            view.Gems = CostGemsView.Create(go.transform);
            view.Log = BattleLogPanel.Create(go.transform);
            view.Chain = ChainStackView.Create(go.transform);
            view.BigCard = BigCardOverlay.Create(go.transform);
            view.Inspect = CardInspectOverlay.Create(go.transform);
            view.Piles = PileListOverlay.Create(go.transform, view.Inspect);
            view.CoinToss = CoinTossOverlay.Create(go.transform);
            return view;
        }

        static TextMeshProUGUI Text(Transform parent, string name, Vector2 anchor, Vector2 offset, float size, TextAlignmentOptions alignment)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
            rect.anchoredPosition = offset;
            rect.sizeDelta = new Vector2(700, 120);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.font = KoreanFont.Get();
            text.fontSize = size;
            text.alignment = alignment;
            text.color = Color.white;
            text.raycastTarget = false; // 글자 영역이 보드 카드 클릭을 가로채지 않게
            return text;
        }

        public string TurnText => _turn.text;
        /// <summary>상대 카드 크게 띄우기.</summary>
        public BigCardOverlay BigCard { get; private set; }
        /// <summary>카드 확인 (짧은 클릭으로 크게 보기).</summary>
        public CardInspectOverlay Inspect { get; private set; }
        /// <summary>묘지·제외 목록 창 (보드의 더미를 누르면).</summary>
        public PileListOverlay Piles { get; private set; }
        /// <summary>코스트 보석 (오른쪽 아래 = 나, 오른쪽 위 = 상대).</summary>
        public CostGemsView Gems { get; private set; }
        /// <summary>행동 기록 패널 (왼쪽).</summary>
        public BattleLogPanel Log { get; private set; }
        /// <summary>체인 표시 (왼쪽 아래).</summary>
        public ChainStackView Chain { get; private set; }
        /// <summary>대전 시작 코인 토스.</summary>
        public CoinTossOverlay CoinToss { get; private set; }

        /// <summary>컴퓨터가 다음 행동을 고르는 동안 "상대가 생각 중…" 표시.</summary>
        public void SetThinking(bool thinking) => _thinking.text = thinking ? "상대가 생각 중…" : "";
        public string HintText => _hint.text;
        /// <summary>온라인 대전: 남은 시간·연결 상태 줄.</summary>
        public void SetOnline(string text, Color color)
        {
            _online.text = text ?? "";
            _online.color = color;
        }
        public string OnlineText => _online.text;
        public UnityEngine.UI.Button EndTurnButton => _endTurn;

        /// <summary>오른쪽 가운데에 "턴 종료" 버튼을 만든다.</summary>
        public void CreateEndTurnButton(System.Action onClick)
        {
            _endTurn = UiFactory.Button(transform, "EndTurn", "턴 종료", new Vector2(1f, 0.5f), new Vector2(-150, 0),
                new Vector2(220, 84), new Color(0.75f, 0.55f, 0.2f), onClick, 34f);
        }

        /// <summary>아래쪽에 안내 문구를 잠깐 띄운다 (행동이 안 되는 이유 등).</summary>
        public void ShowHint(string text, float seconds = 1.8f)
        {
            _hint.text = text;
            StartCoroutine(ClearHint(++_hintVersion, seconds));
        }

        IEnumerator ClearHint(int version, float seconds)
        {
            yield return new WaitForSeconds(seconds);
            if (version == _hintVersion)
                _hint.text = "";
        }
        public string BannerText => _banner.text;

        public void Refresh(GameState state, int viewer)
        {
            _turn.text = $"턴 {state.TurnNumber} · {Whose(state, viewer)}";
            Fill(_lines[0], state.GetPlayer(viewer));
            Fill(_lines[1], state.GetOpponent(viewer));
            var mine = state.GetPlayer(viewer);
            var theirs = state.GetOpponent(viewer);
            Gems.Set(0, mine.AvailableCost, mine.MaxCost, state.Rules.MaxCostLimit);
            Gems.Set(1, theirs.AvailableCost, theirs.MaxCost, state.Rules.MaxCostLimit);
            Redraw();
        }

        public void SetTurn(int turnNumber, bool mine) => _turn.text = $"턴 {turnNumber} · {(mine ? "내 차례" : "상대 차례")}";

        /// <summary>연출 중 코스트만 먼저 바꾼다 (보석 채움·소모 연출). side 0 = 나.</summary>
        public void SetCost(int side, int cost, int maxCost) => Gems.Change(side, cost, maxCost);

        /// <summary>가운데 알림을 잠깐 띄운다. stay면 지우지 않는다.</summary>
        public IEnumerator Banner(string text, Color color, float duration, bool stay = false)
        {
            int version = ++_bannerVersion;
            _banner.text = text;
            _banner.color = color;
            yield return Tween.Wait(duration);
            if (!stay && version == _bannerVersion)
                _banner.text = "";
        }

        static string Whose(GameState state, int viewer)
        {
            if (state.Phase == GamePhase.Finished)
                return state.WinnerIndex == null ? "무승부" : state.WinnerIndex == viewer ? "승리" : "패배";
            return state.ActivePlayerIndex == viewer ? "내 차례" : "상대 차례";
        }

        static void Fill(Line line, PlayerState p)
        {
            line.Hand = p.Hand.Count;
            line.Deck = p.Deck.Count;
        }

        void Redraw()
        {
            _mine.text = _lines[0].ToString();
            _theirs.text = _lines[1].ToString();
        }
    }
}
