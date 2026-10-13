using System;
using System.Collections.Generic;
using TCG.Client.Common;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TCG.Client.CardMaker
{
    /// <summary>
    /// 세로로 줄을 쌓는 편집 칸 (스크롤). 값이 바뀌면 화면이 <see cref="Clear"/> 후 다시 쌓는다.
    /// 줄: 왼쪽 이름표 + 오른쪽 조작(◀ 값 ▶, − 값 +, 켜고 끄는 칩, 버튼, 글 입력).
    /// </summary>
    public sealed class FormColumn
    {
        const float RowHeight = 54f;
        const float Gap = 8f;
        const float LabelWidth = 170f;

        static readonly Color ButtonColor = new Color(0.24f, 0.27f, 0.34f);
        static readonly Color ChipOn = new Color(0.25f, 0.5f, 0.75f);
        static readonly Color ChipOff = new Color(0.2f, 0.22f, 0.27f);
        static readonly Color HeaderColor = new Color(1f, 0.85f, 0.5f);

        readonly float _width;
        float _y;

        public RectTransform Content { get; }
        public ScrollRect Scroll { get; }

        public static FormColumn Create(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size)
        {
            return new FormColumn(parent, name, anchor, position, size);
        }

        FormColumn(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size)
        {
            _width = size.x - 30f;
            var viewport = UiFactory.Rect(parent, name, anchor, position, size);
            viewport.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.03f);
            viewport.gameObject.AddComponent<RectMask2D>();

            Content = UiFactory.Rect(viewport, "Content", new Vector2(0.5f, 1f), Vector2.zero, new Vector2(size.x, 0));
            Content.pivot = new Vector2(0.5f, 1f);

            Scroll = viewport.gameObject.AddComponent<ScrollRect>();
            Scroll.viewport = viewport;
            Scroll.content = Content;
            Scroll.horizontal = false;
            Scroll.movementType = ScrollRect.MovementType.Clamped;
            Scroll.scrollSensitivity = 40f;
        }

        public void Clear()
        {
            for (int i = Content.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(Content.GetChild(i).gameObject);
            _y = -12f;
        }

        /// <summary>쌓은 높이로 스크롤 내용 크기를 맞춘다.</summary>
        public void Finish() => Content.sizeDelta = new Vector2(Content.sizeDelta.x, -_y + 20f);

        public void Space(float height) => _y -= height;

        /// <summary>폭 전체를 쓰는 빈 줄 (안을 직접 꾸민다).</summary>
        public RectTransform Block(string name, float height)
        {
            var rect = UiFactory.Rect(Content, name, new Vector2(0.5f, 1f), new Vector2(0, _y - height * 0.5f), new Vector2(_width, height));
            _y -= height + Gap;
            return rect;
        }

        public float Width => _width;

        public void Header(string text)
        {
            _y -= 6f;
            var t = UiFactory.Text(Content, "Header", text, new Vector2(0.5f, 1f), new Vector2(0, _y - 22f), new Vector2(_width, 44f), 30,
                TextAlignmentOptions.Left);
            t.color = HeaderColor;
            t.fontStyle = FontStyles.Bold;
            UiFactory.Panel(Content, "Line", new Vector2(0.5f, 1f), new Vector2(0, _y - 46f), new Vector2(_width, 2f), new Color(1f, 1f, 1f, 0.15f));
            _y -= 54f;
        }

        public TextMeshProUGUI Note(string text, Color color, float fontSize = 24f)
        {
            var t = UiFactory.Text(Content, "Note", text, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(_width, 10f), fontSize,
                TextAlignmentOptions.TopLeft);
            t.color = color;
            t.textWrappingMode = TextWrappingModes.Normal;
            float height = Mathf.Max(fontSize + 8f, t.GetPreferredValues(text, _width, 0f).y);
            t.rectTransform.sizeDelta = new Vector2(_width, height);
            t.rectTransform.anchoredPosition = new Vector2(0, _y - height * 0.5f);
            _y -= height + Gap;
            return t;
        }

        RectTransform Row(string label)
        {
            var row = UiFactory.Rect(Content, "Row " + label, new Vector2(0.5f, 1f), new Vector2(0, _y - RowHeight * 0.5f), new Vector2(_width, RowHeight));
            if (!string.IsNullOrEmpty(label))
                UiFactory.Text(row, "Label", label, new Vector2(0f, 0.5f), new Vector2(LabelWidth * 0.5f, 0), new Vector2(LabelWidth, RowHeight), 26,
                    TextAlignmentOptions.Left).color = new Color(0.85f, 0.87f, 0.92f);
            _y -= RowHeight + Gap;
            return row;
        }

        float ControlWidth => _width - LabelWidth;
        Vector2 ControlCenter => new Vector2(LabelWidth + ControlWidth * 0.5f, 0);

        /// <summary>◀ 값 ▶ (목록에서 고르기).</summary>
        public void Cycle(string label, string current, Action<int> step)
        {
            var row = Row(label);
            float w = ControlWidth;
            var left = new Vector2(LabelWidth + 30f, 0);
            UiFactory.Button(row, "Prev", "◀", new Vector2(0f, 0.5f), left, new Vector2(56f, RowHeight - 6f), ButtonColor, () => step(-1), 26);
            UiFactory.Text(row, "Value", current, new Vector2(0f, 0.5f), ControlCenter, new Vector2(w - 140f, RowHeight), 26);
            UiFactory.Button(row, "Next", "▶", new Vector2(0f, 0.5f), new Vector2(LabelWidth + w - 30f, 0), new Vector2(56f, RowHeight - 6f), ButtonColor,
                () => step(1), 26);
        }

        public void CycleEnum<T>(string label, T current, IReadOnlyList<T> options, Func<T, string> name, Action<T> set)
        {
            int index = IndexOf(options, current);
            Cycle(label, name(current), d =>
            {
                int next = ((index < 0 ? 0 : index) + d + options.Count) % options.Count;
                set(options[next]);
            });
        }

        /// <summary>− 값 + (정수).</summary>
        public void Stepper(string label, int value, int min, int max, Action<int> set)
        {
            var row = Row(label);
            float w = ControlWidth;
            var minus = UiFactory.Button(row, "Minus", "−", new Vector2(0f, 0.5f), new Vector2(LabelWidth + 30f, 0), new Vector2(56f, RowHeight - 6f),
                ButtonColor, () => set(Mathf.Max(min, value - 1)), 30);
            minus.interactable = value > min;
            UiFactory.Text(row, "Value", value.ToString(), new Vector2(0f, 0.5f), ControlCenter, new Vector2(w - 140f, RowHeight), 30);
            var plus = UiFactory.Button(row, "Plus", "+", new Vector2(0f, 0.5f), new Vector2(LabelWidth + w - 30f, 0), new Vector2(56f, RowHeight - 6f),
                ButtonColor, () => set(Mathf.Min(max, value + 1)), 30);
            plus.interactable = value < max;
        }

        /// <summary>켜고 끄는 칩 여러 개 (한 줄에 다 들어가게 너비를 나눈다).</summary>
        public void Chips(string label, IReadOnlyList<(string text, bool on, Action toggle)> chips)
        {
            var row = Row(label);
            if (chips.Count == 0)
            {
                UiFactory.Text(row, "None", "없음", new Vector2(0f, 0.5f), ControlCenter, new Vector2(ControlWidth, RowHeight), 24)
                    .color = new Color(1f, 1f, 1f, 0.4f);
                return;
            }
            float each = ControlWidth / chips.Count;
            for (int i = 0; i < chips.Count; i++)
            {
                var chip = chips[i];
                UiFactory.Button(row, "Chip " + chip.text, chip.text, new Vector2(0f, 0.5f), new Vector2(LabelWidth + each * (i + 0.5f), 0),
                    new Vector2(each - 8f, RowHeight - 6f), chip.on ? ChipOn : ChipOff, chip.toggle, 24);
            }
        }

        /// <summary>버튼 여러 개를 한 줄에.</summary>
        public void Buttons(string label, params (string text, Action action)[] buttons)
        {
            var row = Row(label);
            float start = string.IsNullOrEmpty(label) ? 0f : LabelWidth;
            float each = (_width - start) / buttons.Length;
            for (int i = 0; i < buttons.Length; i++)
            {
                var b = buttons[i];
                UiFactory.Button(row, "Button " + b.text, b.text, new Vector2(0f, 0.5f), new Vector2(start + each * (i + 0.5f), 0),
                    new Vector2(each - 8f, RowHeight - 6f), ButtonColor, b.action, 24);
            }
        }

        public TMP_InputField Input(string label, string value, int maxLength, Action<string> changed)
        {
            var row = Row(label);
            var field = UiFactory.InputField(row, "Input", new Vector2(0f, 0.5f), ControlCenter, new Vector2(ControlWidth, RowHeight - 6f), 26);
            field.characterLimit = maxLength;
            field.SetTextWithoutNotify(value ?? "");
            field.onValueChanged.AddListener(v => changed(v));
            return field;
        }

        static int IndexOf<T>(IReadOnlyList<T> list, T value)
        {
            var cmp = EqualityComparer<T>.Default;
            for (int i = 0; i < list.Count; i++)
            {
                if (cmp.Equals(list[i], value))
                    return i;
            }
            return -1;
        }
    }
}
