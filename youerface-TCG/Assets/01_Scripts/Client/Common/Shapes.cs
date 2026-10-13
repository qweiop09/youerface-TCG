using TMPro;
using UnityEngine;

namespace TCG.Client.Common
{
    /// <summary>임시 그림용 기본 도형·글자 생성 도우미. 모두 XZ 평면(바닥)에 눕혀 위(+Y)를 향하게 만든다.</summary>
    public static class Shapes
    {
        public static GameObject Box(Transform parent, string name, Vector3 localPos, Vector3 size, Color color, bool unlit = false)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Setup(go, parent, name, localPos, color, unlit);
            go.transform.localScale = size;
            return go;
        }

        /// <summary>위(+Y)를 향한 그림 판. 그림의 위쪽이 +Z.</summary>
        public static GameObject Picture(Transform parent, string name, Vector3 localPos, float width, float height, Texture texture)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localScale = new Vector3(width, height, 1f);
            var collider = go.GetComponent<Collider>();
            if (collider != null)
                DestroyImmediateOrDeferred(collider);
            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = MaterialCache.Textured(texture);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        /// <summary>납작한 원판 (지름 diameter, 두께 height).</summary>
        public static GameObject Disc(Transform parent, string name, Vector3 localPos, float diameter, float height, Color color, bool unlit = false)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Setup(go, parent, name, localPos, color, unlit);
            go.transform.localScale = new Vector3(diameter, height * 0.5f, diameter);
            return go;
        }

        /// <summary>바닥에 눕힌 글자. 글자의 위쪽이 +Z를 향한다.</summary>
        public static TextMeshPro Label(Transform parent, string name, string text, Vector3 localPos, float fontSize, Color color,
            float width = 2f, float height = 0.5f, TextAlignmentOptions alignment = TextAlignmentOptions.Center, bool ellipsis = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            var tmp = go.AddComponent<TextMeshPro>();
            tmp.font = KoreanFont.Get();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.alignment = alignment;
            // 숫자·짧은 글자는 칸을 넘쳐도 보이게 하고, 이름처럼 긴 글자만 말줄임한다.
            tmp.textWrappingMode = ellipsis ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            tmp.overflowMode = ellipsis ? TextOverflowModes.Ellipsis : TextOverflowModes.Overflow;
            tmp.rectTransform.sizeDelta = new Vector2(width, height);
            return tmp;
        }

        public static TextMeshPro Bold(TextMeshPro text)
        {
            text.fontStyle = FontStyles.Bold;
            return text;
        }

        static void Setup(GameObject go, Transform parent, string name, Vector3 localPos, Color color, bool unlit)
        {
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var collider = go.GetComponent<Collider>();
            if (collider != null)
                DestroyImmediateOrDeferred(collider);
            go.GetComponent<Renderer>().sharedMaterial = MaterialCache.Get(color, unlit);
        }

        /// <summary>에디터(Play 아님)에서도 화면을 만들 수 있게 상황에 맞는 삭제를 쓴다.</summary>
        public static void DestroyImmediateOrDeferred(Object obj)
        {
            if (Application.isPlaying)
                Object.Destroy(obj);
            else
                Object.DestroyImmediate(obj);
        }
    }
}
