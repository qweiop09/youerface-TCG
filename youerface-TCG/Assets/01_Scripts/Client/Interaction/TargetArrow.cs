using TCG.Client.Common;
using UnityEngine;

namespace TCG.Client.Interaction
{
    /// <summary>공격·대상 지정 화살표: 시작점에서 포인터까지 휘어진 선 + 끝 원판.</summary>
    public sealed class TargetArrow : MonoBehaviour
    {
        const int Points = 24;
        LineRenderer _line;
        Transform _head;

        public static TargetArrow Create(Transform parent)
        {
            var go = new GameObject("TargetArrow");
            go.transform.SetParent(parent, false);
            var arrow = go.AddComponent<TargetArrow>();
            arrow._line = go.AddComponent<LineRenderer>();
            arrow._line.positionCount = Points;
            arrow._line.widthMultiplier = 0.14f;
            arrow._line.numCapVertices = 4;
            arrow._line.useWorldSpace = true;
            arrow._line.sharedMaterial = MaterialCache.Get(new Color(1f, 0.55f, 0.2f), unlit: true);
            arrow._head = Shapes.Disc(go.transform, "Head", Vector3.zero, 0.45f, 0.04f, new Color(1f, 0.55f, 0.2f), unlit: true).transform;
            go.SetActive(false);
            return arrow;
        }

        public void Show(Vector3 from, Vector3 to, bool valid)
        {
            gameObject.SetActive(true);
            var color = valid ? new Color(0.4f, 1f, 0.45f) : new Color(1f, 0.55f, 0.2f);
            _line.sharedMaterial = MaterialCache.Get(color, unlit: true);
            _head.GetComponent<Renderer>().sharedMaterial = MaterialCache.Get(color, unlit: true);

            float height = Mathf.Clamp(Vector3.Distance(from, to) * 0.25f, 0.5f, 2.5f);
            for (int i = 0; i < Points; i++)
            {
                float t = i / (float)(Points - 1);
                _line.SetPosition(i, Vector3.Lerp(from, to, t) + Vector3.up * (height * 4f * t * (1f - t) + 0.3f));
            }
            _head.position = to + Vector3.up * 0.3f;
        }

        public void Hide() => gameObject.SetActive(false);
    }
}
