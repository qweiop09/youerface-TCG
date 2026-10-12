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

        public static void SetLabel(Button button, string label) =>
            button.GetComponentInChildren<TextMeshProUGUI>().text = label;
    }
}
