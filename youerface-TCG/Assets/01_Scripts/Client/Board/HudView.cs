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
            public int Cost, MaxCost, Hand, Deck;
            public override string ToString() => $"{Who}   코스트 {Cost}/{MaxCost}   손패 {Hand}   덱 {Deck}";
        }

        TextMeshProUGUI _turn, _mine, _theirs, _banner;
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

            var view = go.AddComponent<HudView>();
            view._turn = Text(go.transform, "Turn", new Vector2(0.5f, 1f), new Vector2(0, -24), 34, TextAlignmentOptions.Top);
            view._theirs = Text(go.transform, "Opponent", new Vector2(0f, 1f), new Vector2(32, -24), 28, TextAlignmentOptions.TopLeft);
            view._mine = Text(go.transform, "Me", new Vector2(0f, 0f), new Vector2(32, 24), 28, TextAlignmentOptions.BottomLeft);
            view._banner = Text(go.transform, "Banner", new Vector2(0.5f, 0.5f), new Vector2(0, 40), 72, TextAlignmentOptions.Center);
            view._banner.rectTransform.sizeDelta = new Vector2(1400, 200);
            view._banner.fontStyle = FontStyles.Bold;
            view._banner.text = "";
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
            return text;
        }

        public string TurnText => _turn.text;
        public string BannerText => _banner.text;

        public void Refresh(GameState state, int viewer)
        {
            _turn.text = $"턴 {state.TurnNumber} · {Whose(state, viewer)}";
            Fill(_lines[0], state.GetPlayer(viewer));
            Fill(_lines[1], state.GetOpponent(viewer));
            Redraw();
        }

        public void SetTurn(int turnNumber, bool mine) => _turn.text = $"턴 {turnNumber} · {(mine ? "내 차례" : "상대 차례")}";

        /// <summary>연출 중 코스트만 먼저 바꾼다. side 0 = 나.</summary>
        public void SetCost(int side, int cost, int maxCost)
        {
            _lines[side].Cost = cost;
            _lines[side].MaxCost = maxCost;
            Redraw();
        }

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
            line.Cost = p.AvailableCost;
            line.MaxCost = p.MaxCost;
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
