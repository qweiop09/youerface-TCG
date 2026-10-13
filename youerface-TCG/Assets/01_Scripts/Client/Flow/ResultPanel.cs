using TCG.Client.Common;
using TCG.Core.AI;
using TCG.Core.State;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TCG.Client.Flow
{
    /// <summary>결과 화면: 대전이 끝나고 연출이 다 끝나면 HUD 위에 덮는다. 승리/패배/무승부 + "다시 하기", "난이도 선택으로".</summary>
    public sealed class ResultPanel : MonoBehaviour
    {
        public string Title { get; private set; }
        public Button RematchButton { get; private set; }
        public Button TitleButton { get; private set; }

        public static string TitleFor(GameState state, int viewer) =>
            state.WinnerIndex == null ? "무승부" : state.WinnerIndex == viewer ? "승리" : "패배";

        public static ResultPanel Create(Transform hud, GameState state, int viewer, AiDifficulty difficulty)
        {
            var rect = UiFactory.Stretch(hud, "Result");
            var panel = rect.gameObject.AddComponent<ResultPanel>();
            var dim = rect.gameObject.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.72f); // 아래 보드·버튼 클릭을 막는다

            panel.Title = TitleFor(state, viewer);
            var color = state.WinnerIndex == null ? Color.white : state.WinnerIndex == viewer ? new Color(1f, 0.85f, 0.4f) : new Color(0.75f, 0.8f, 0.9f);
            var title = UiFactory.Text(rect, "Title", panel.Title, new Vector2(0.5f, 0.5f), new Vector2(0, 170), new Vector2(1000, 180), 120);
            title.fontStyle = FontStyles.Bold;
            title.color = color;
            UiFactory.Text(rect, "Info", $"상대 난이도 {KoreanTerms.Of(difficulty)} · {state.TurnNumber}턴", new Vector2(0.5f, 0.5f),
                new Vector2(0, 50), new Vector2(1000, 60), 32).color = new Color(0.8f, 0.82f, 0.88f);

            panel.RematchButton = UiFactory.Button(rect, "Rematch", "다시 하기", new Vector2(0.5f, 0.5f), new Vector2(-210, -110),
                new Vector2(360, 100), new Color(0.75f, 0.55f, 0.2f), BattleLaunch.Rematch, 38f);
            panel.TitleButton = UiFactory.Button(rect, "ToTitle", "난이도 선택으로", new Vector2(0.5f, 0.5f), new Vector2(210, -110),
                new Vector2(360, 100), new Color(0.25f, 0.32f, 0.45f), BattleLaunch.ToTitle, 34f);
            return panel;
        }
    }
}
