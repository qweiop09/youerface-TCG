using TCG.Client.Common;
using TCG.Core.State;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TCG.Client.Board
{
    /// <summary>화면 위에 덮는 정보 표시 (턴, 코스트, 손패·덱 장수). 카메라 기준 캔버스라 화면 캡처에도 찍힌다.</summary>
    public sealed class HudView : MonoBehaviour
    {
        TextMeshProUGUI _turn, _mine, _theirs;

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

        public void Refresh(GameState state, int viewer)
        {
            var me = state.GetPlayer(viewer);
            var them = state.GetOpponent(viewer);
            string whose = state.Phase == GamePhase.Finished
                ? (state.WinnerIndex == null ? "무승부" : state.WinnerIndex == viewer ? "승리" : "패배")
                : state.ActivePlayerIndex == viewer ? "내 차례" : "상대 차례";
            _turn.text = $"턴 {state.TurnNumber} · {whose}";
            _theirs.text = Line("상대", them);
            _mine.text = Line("나", me);
        }

        static string Line(string who, PlayerState p) =>
            $"{who}   코스트 {p.AvailableCost}/{p.MaxCost}   손패 {p.Hand.Count}   덱 {p.Deck.Count}";
    }
}
