using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TCG.Client.Common
{
    /// <summary>코드로 만드는 uGUI 요소 (패널, 버튼, 글자). 기준 해상도 1920x1080.</summary>
    public static class UiFactory
    {
        public static RectTransform Rect(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        public static Image Panel(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size, Color color)
        {
            var rect = Rect(parent, name, anchor, position, size);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            return image;
        }

        public static TextMeshProUGUI Text(Transform parent, string name, string text, Vector2 anchor, Vector2 position, Vector2 size,
            float fontSize, TextAlignmentOptions alignment = TextAlignmentOptions.Center)
        {
            var rect = Rect(parent, name, anchor, position, size);
            var tmp = rect.gameObject.AddComponent<TextMeshProUGUI>();
            tmp.font = KoreanFont.Get();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.alignment = alignment;
            tmp.color = Color.white;
            tmp.raycastTarget = false;
            return tmp;
        }

        public static Button Button(Transform parent, string name, string label, Vector2 anchor, Vector2 position, Vector2 size,
            Color color, Action onClick, float fontSize = 30f)
        {
            var image = Panel(parent, name, anchor, position, size, color);
            var button = image.gameObject.AddComponent<Button>();
            var colors = button.colors;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f);
            colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.8f);
            button.colors = colors;
            if (onClick != null)
                button.onClick.AddListener(() => onClick());
            var text = Text(image.transform, "Label", label, new Vector2(0.5f, 0.5f), Vector2.zero, size, fontSize);
            text.rectTransform.anchorMin = Vector2.zero;
            text.rectTransform.anchorMax = Vector2.one;
            text.rectTransform.sizeDelta = Vector2.zero;
            return button;
        }

        /// <summary>
        /// 얇은 선 상자: 안이 비어 있는 테두리 (네 변을 얇은 Image로). 왼쪽 위 테두리에 이름표를 얹는다.
        /// 반환한 RectTransform 안에 내용을 넣는다.
        /// </summary>
        public static RectTransform OutlineBox(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size,
            Color lineColor, string label, Color background, float thickness = 2f)
        {
            var box = Rect(parent, name, anchor, position, size);
            Edge(box, "Top", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, thickness), lineColor);
            Edge(box, "Bottom", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, thickness), lineColor);
            Edge(box, "Left", new Vector2(0, 0), new Vector2(0, 1), new Vector2(thickness, 0), lineColor);
            Edge(box, "Right", new Vector2(1, 0), new Vector2(1, 1), new Vector2(thickness, 0), lineColor);

            if (!string.IsNullOrEmpty(label))
            {
                // 선을 끊고 그 위에 이름표 (배경색으로 선을 가린다)
                var tag = Panel(box, "Label", new Vector2(0, 1), new Vector2(24 + label.Length * 13, 0), new Vector2(40 + label.Length * 26, 34), background);
                tag.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                var text = Text(tag.transform, "Text", label, new Vector2(0.5f, 0.5f), Vector2.zero, tag.rectTransform.sizeDelta, 24);
                text.color = lineColor;
            }
            return box;
        }

        static void Edge(RectTransform box, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(box, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = size;
            var image = go.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
        }

        public static void SetLabel(Button button, string label) =>
            button.GetComponentInChildren<TextMeshProUGUI>().text = label;
    }
}
