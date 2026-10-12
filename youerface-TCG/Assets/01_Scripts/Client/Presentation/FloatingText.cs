using System.Collections;
using TCG.Client.Common;
using TMPro;
using UnityEngine;

namespace TCG.Client.Presentation
{
    /// <summary>위로 떠오르며 사라지는 글자 (피해 숫자, "무효" 등). 카메라를 향한다.</summary>
    public static class FloatingText
    {
        public static IEnumerator Play(Transform parent, Camera camera, string text, Color color, Vector3 position, float size = 5f, float duration = 0.8f)
        {
            var go = new GameObject($"Float {text}");
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            var tmp = go.AddComponent<TextMeshPro>();
            tmp.font = KoreanFont.Get();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.rectTransform.sizeDelta = new Vector2(4f, 1f);
            if (camera != null)
                go.transform.rotation = Quaternion.LookRotation(camera.transform.forward, camera.transform.up);

            for (float t = 0; t < duration; t += PresentationTime.DeltaTime)
            {
                float k = Mathf.Clamp01(t / duration);
                go.transform.position = position + Vector3.up * (0.4f + k * 0.9f);
                tmp.color = new Color(color.r, color.g, color.b, 1f - k * k);
                yield return null;
            }
            Shapes.DestroyImmediateOrDeferred(go);
        }
    }
}
