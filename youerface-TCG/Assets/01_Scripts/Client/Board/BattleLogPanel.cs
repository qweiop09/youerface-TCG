using System.Collections.Generic;
using TCG.Client.Common;
using TCG.Client.Presentation;
using TCG.Core.Events;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TCG.Client.Board
{
    /// <summary>
    /// 행동 기록 패널 (GameDesign §11.3): 화면 왼쪽, 연출이 재생되는 순서대로 한 줄씩 쌓는다.
    /// 휠로 스크롤, 맨 아래를 보고 있을 때만 새 줄을 따라 내려간다. 머리의 버튼으로 접고 편다.
    /// </summary>
    public sealed class BattleLogPanel : MonoBehaviour
    {
        public const int MaxLines = 200;
        const float Width = 340f;
        const float Height = 440f;
        const float HeaderHeight = 44f;

        static readonly Color MineColor = new Color(0.62f, 0.8f, 1f);
        static readonly Color TheirsColor = new Color(1f, 0.7f, 0.6f);
        static readonly Color NeutralColor = new Color(0.88f, 0.88f, 0.86f);
        static readonly Color SeparatorColor = new Color(1f, 0.85f, 0.45f);

        readonly List<string> _lines = new List<string>();
        readonly List<TextMeshProUGUI> _views = new List<TextMeshProUGUI>();
        RectTransform _body;
        RectTransform _content;
        ScrollRect _scroll;
        Button _toggle;
        bool _followBottom = true;

        public bool Expanded { get; private set; } = true;
        /// <summary>쌓인 줄 (태그 포함 원문, 테스트·확인용).</summary>
        public IReadOnlyList<string> Lines => _lines;

        public static BattleLogPanel Create(Transform canvas)
        {
            var root = UiFactory.Rect(canvas, "BattleLog", new Vector2(0f, 1f), Vector2.zero, new Vector2(Width, Height));
            root.pivot = new Vector2(0f, 1f);
            root.anchoredPosition = new Vector2(16f, -100f);
            var panel = root.gameObject.AddComponent<BattleLogPanel>();

            var header = UiFactory.Panel(root, "Header", new Vector2(0.5f, 1f), new Vector2(0, -HeaderHeight * 0.5f),
                new Vector2(Width, HeaderHeight), new Color(0.08f, 0.1f, 0.14f, 0.9f));
            UiFactory.Text(header.transform, "Title", "행동 기록", new Vector2(0f, 0.5f), new Vector2(80f, 0f), new Vector2(150f, HeaderHeight), 24)
                .fontStyle = FontStyles.Bold;
            panel._toggle = UiFactory.Button(header.transform, "Toggle", "접기", new Vector2(1f, 0.5f), new Vector2(-46f, 0f),
                new Vector2(80f, 34f), new Color(0.24f, 0.28f, 0.36f), panel.Toggle, 20f);

            var body = UiFactory.Panel(root, "Body", new Vector2(0.5f, 1f), Vector2.zero, new Vector2(Width, Height - HeaderHeight),
                new Color(0.05f, 0.06f, 0.09f, 0.72f));
            panel._body = body.rectTransform;
            panel._body.pivot = new Vector2(0.5f, 1f);
            panel._body.anchoredPosition = new Vector2(0f, -HeaderHeight);
            body.gameObject.AddComponent<RectMask2D>();

            panel._content = UiFactory.Rect(body.transform, "Content", new Vector2(0.5f, 1f), Vector2.zero, new Vector2(Width - 20f, 0f));
            panel._content.pivot = new Vector2(0.5f, 1f);
            var layout = panel._content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 2f;
            layout.padding = new RectOffset(4, 4, 6, 6);
            panel._content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            panel._scroll = body.gameObject.AddComponent<ScrollRect>();
            panel._scroll.content = panel._content;
            panel._scroll.viewport = panel._body;
            panel._scroll.horizontal = false;
            panel._scroll.movementType = ScrollRect.MovementType.Clamped;
            panel._scroll.scrollSensitivity = 30f;
            panel._scroll.onValueChanged.AddListener(_ => panel._followBottom = panel._scroll.verticalNormalizedPosition <= 0.01f
                                                                              || panel._content.rect.height <= panel._body.rect.height);
            return panel;
        }

        readonly List<GameEvent> _deferred = new List<GameEvent>();

        /// <summary>
        /// 대전 시작 이벤트 중 연출로 재생하지 않는 것(첫 턴 시작·드로우 등). 코인 토스가 끝나면 <see cref="FlushDeferred"/>로 기록한다.
        /// 시작 손패 드로우(첫 턴 시작 전)는 넣지 않는다.
        /// </summary>
        public void Defer(IReadOnlyList<GameEvent> startEvents)
        {
            _deferred.Clear();
            bool turnStarted = false;
            foreach (var e in startEvents)
            {
                turnStarted |= e is TurnStartedEvent;
                if (turnStarted)
                    _deferred.Add(e);
            }
        }

        /// <summary>코인 토스 결과 줄과 미뤄 둔 대전 시작 기록을 쓴다.</summary>
        public void FlushDeferred(PresentationContext context, int firstPlayer)
        {
            Add(BattleLogText.FirstPlayer(context, firstPlayer));
            foreach (var e in _deferred)
                Record(context, e);
            _deferred.Clear();
        }

        /// <summary>연출이 이벤트 하나를 재생하기 시작할 때.</summary>
        public void Record(PresentationContext context, GameEvent e)
        {
            var line = BattleLogText.Describe(context, e);
            if (line.HasValue)
                Add(line.Value);
        }

        public void Add(BattleLogLine line)
        {
            var color = line.Separator ? SeparatorColor : line.Side == 0 ? MineColor : line.Side == 1 ? TheirsColor : NeutralColor;
            string text = line.Separator ? $"— {line.Text} —" : line.Text;
            _lines.Add(text);

            TextMeshProUGUI view;
            if (_views.Count >= MaxLines)
            {
                // 가장 오래된 줄을 재사용해 맨 아래로.
                view = _views[0];
                _views.RemoveAt(0);
                _lines.RemoveAt(0);
                view.transform.SetAsLastSibling();
            }
            else
            {
                view = UiFactory.Text(_content, "Line", "", new Vector2(0.5f, 1f), Vector2.zero, new Vector2(Width - 28f, 26f), 20,
                    TextAlignmentOptions.TopLeft);
                view.textWrappingMode = TextWrappingModes.Normal;
                view.richText = true;
            }
            _views.Add(view);
            view.text = text;
            view.color = color;
            view.alignment = line.Separator ? TextAlignmentOptions.Top : TextAlignmentOptions.TopLeft;
            view.fontStyle = line.Separator ? FontStyles.Bold : FontStyles.Normal;

            if (_followBottom)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
                _scroll.verticalNormalizedPosition = 0f;
            }
        }

        public void Toggle()
        {
            Expanded = !Expanded;
            _body.gameObject.SetActive(Expanded);
            UiFactory.SetLabel(_toggle, Expanded ? "접기" : "펴기");
            if (Expanded)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
                _scroll.verticalNormalizedPosition = 0f;
                _followBottom = true;
            }
        }
    }
}
