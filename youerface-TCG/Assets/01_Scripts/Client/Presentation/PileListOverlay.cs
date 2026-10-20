using System.Collections.Generic;
using TCG.Client.Cards;
using TCG.Client.Common;
using TCG.Core.State;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace TCG.Client.Presentation
{
    /// <summary>
    /// 묘지·제외 목록 창 (유희왕처럼 공개 정보 — 상대 것도 볼 수 있다). 보드의 더미(<see cref="Board.GraveView"/>)를 누르면 연다.
    /// 카드를 작게 격자로 보여 주고(먼저 들어온 카드가 앞), 카드를 누르면 카드 확인 창으로 자세히 본다.
    /// 닫기: 닫기 버튼·빈 곳 클릭·Esc (카드 확인 창이 떠 있으면 Esc는 그 창부터 닫는다).
    /// </summary>
    public sealed class PileListOverlay : MonoBehaviour
    {
        const float CardScale = 0.42f;
        const int Columns = 6;
        const float CellW = CardDetailView.Width * CardScale + 18f;
        const float CellH = CardDetailView.Height * CardScale + 18f;

        TextMeshProUGUI _title;
        RectTransform _content;
        RectTransform _panel;
        CardInspectOverlay _inspect;
        int _openedFrame = -1;

        public bool IsOpen => gameObject.activeSelf;
        /// <summary>닫은 프레임 (같은 클릭·Esc가 보드 입력이나 다른 창으로 이어지지 않게).</summary>
        public int ClosedFrame { get; private set; } = -1;

        public static PileListOverlay Create(Transform canvas, CardInspectOverlay inspect)
        {
            var root = UiFactory.Stretch(canvas, "PileList");
            var overlay = root.gameObject.AddComponent<PileListOverlay>();
            overlay._inspect = inspect;
            root.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.65f);

            float width = CellW * Columns + 60f;
            var panel = UiFactory.Panel(root, "Panel", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(width, 860), new Color(0.12f, 0.13f, 0.16f, 0.98f));
            overlay._panel = panel.rectTransform;
            overlay._title = UiFactory.Text(panel.transform, "Title", "", new Vector2(0.5f, 1f), new Vector2(0, -40), new Vector2(width - 200, 60), 36);
            overlay._title.fontStyle = FontStyles.Bold;
            UiFactory.Button(panel.transform, "Close", "닫기", new Vector2(1f, 1f), new Vector2(-80, -40), new Vector2(120, 54),
                new Color(0.3f, 0.32f, 0.38f), overlay.Close, 26);

            // 스크롤 목록
            var viewport = UiFactory.Rect(panel.transform, "Viewport", new Vector2(0.5f, 0f), new Vector2(0, 390), new Vector2(width - 40, 760));
            viewport.gameObject.AddComponent<RectMask2D>();
            viewport.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.01f); // 휠·끌기 받기
            overlay._content = UiFactory.Rect(viewport, "Content", new Vector2(0.5f, 1f), Vector2.zero, new Vector2(width - 40, 0));
            overlay._content.pivot = new Vector2(0.5f, 1f);
            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = overlay._content;
            scroll.horizontal = false;
            scroll.scrollSensitivity = 40f;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            root.gameObject.SetActive(false);
            return overlay;
        }

        public void Show(GameState state, int ownerIndex, ZoneType zone, bool mine)
        {
            var list = state.GetPlayer(ownerIndex).GetZone(zone).Entities;
            _title.text = $"{(mine ? "내" : "상대")} {(zone == ZoneType.Exile ? "제외" : "묘지")} ({list.Count}장)";

            for (int i = _content.childCount - 1; i >= 0; i--)
                Destroy(_content.GetChild(i).gameObject);

            int rows = (list.Count + Columns - 1) / Columns;
            _content.sizeDelta = new Vector2(_content.sizeDelta.x, Mathf.Max(1, rows) * CellH + 10f);
            _content.anchoredPosition = Vector2.zero;
            if (list.Count == 0)
            {
                UiFactory.Text(_content, "Empty", "비어 있습니다", new Vector2(0.5f, 1f), new Vector2(0, -60), new Vector2(600, 60), 28)
                    .color = new Color(1, 1, 1, 0.6f);
            }

            float left = -(Columns - 1) * CellW * 0.5f;
            for (int i = 0; i < list.Count; i++)
            {
                var entity = list[i];
                var cell = UiFactory.Rect(_content, "Card " + entity.Id, new Vector2(0.5f, 1f),
                    new Vector2(left + (i % Columns) * CellW, -(i / Columns) * CellH - CellH * 0.5f), new Vector2(CardDetailView.Width, CardDetailView.Height));
                cell.localScale = Vector3.one * CardScale;
                var card = CardDetailView.Create(cell);
                card.SideNotes = false;
                card.Show(entity);
                var button = card.gameObject.AddComponent<Button>();
                button.onClick.AddListener(() => _inspect.Show(entity));
            }

            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            _openedFrame = Time.frameCount;
        }

        public void Close()
        {
            if (!IsOpen)
                return;
            gameObject.SetActive(false);
            ClosedFrame = Time.frameCount;
        }

        void Update()
        {
            if (Time.frameCount == _openedFrame)
                return;
            // 카드 확인 창이 위에 떠 있거나 방금 닫혔으면 그 창이 먼저 입력을 받는다.
            if (_inspect.IsOpen || _inspect.ClosedFrame == Time.frameCount)
                return;
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                Close();
                return;
            }
            // 판 밖(어두운 곳)을 누르면 닫는다.
            var pointer = Pointer.current;
            if (pointer != null && pointer.press.wasPressedThisFrame
                && !RectTransformUtility.RectangleContainsScreenPoint(_panel, pointer.position.ReadValue(), null))
                Close();
        }
    }
}
