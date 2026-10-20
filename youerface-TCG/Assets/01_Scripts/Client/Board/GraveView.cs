using TCG.Client.Cards;
using TCG.Client.Common;
using TCG.Core.State;
using TMPro;
using UnityEngine;

namespace TCG.Client.Board
{
    /// <summary>
    /// 묘지·제외(소멸) 더미 (유희왕의 묘지처럼 덱 옆). 맨 위 카드(종류 색 + 이름)와 장수를 보여 주고,
    /// 클릭하면 <see cref="BattleInput"/>이 목록 창(<see cref="Presentation.PileListOverlay"/>)을 연다.
    /// </summary>
    public sealed class GraveView : MonoBehaviour
    {
        public int OwnerIndex { get; private set; }
        public ZoneType Zone { get; private set; }

        GameObject _top;
        Renderer _topBody;
        TextMeshPro _topName, _count;

        public static GraveView Create(Transform parent, Vector3 position, int ownerIndex, ZoneType zone)
        {
            var go = new GameObject(zone == ZoneType.Exile ? "Exile" : "Graveyard");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            var view = go.AddComponent<GraveView>();
            view.OwnerIndex = ownerIndex;
            view.Zone = zone;

            var collider = go.AddComponent<BoxCollider>();
            collider.size = new Vector3(CardView.Width, 0.1f, CardView.Height);
            collider.center = new Vector3(0, 0.05f, 0);

            Shapes.Box(go.transform, "Plate", new Vector3(0, -0.004f, 0), new Vector3(CardView.Width + 0.1f, 0.01f, CardView.Height + 0.1f),
                new Color(0.12f, 0.14f, 0.16f));
            view._top = new GameObject("Top");
            view._top.transform.SetParent(go.transform, false);
            view._topBody = Shapes.Box(view._top.transform, "Body", new Vector3(0, 0.02f, 0), new Vector3(CardView.Width, 0.03f, CardView.Height),
                Color.gray).GetComponent<Renderer>();
            view._topName = Shapes.Label(view._top.transform, "Name", "", new Vector3(0, 0.04f, 0.15f), 1.5f, CardPalette.Text, 0.9f, 0.4f, ellipsis: true);

            view._count = Shapes.Bold(Shapes.Label(go.transform, "Count", "", new Vector3(0, 0.06f, -0.25f), 3f, Color.white, 1f, 0.5f));
            Shapes.Label(go.transform, "Title", zone == ZoneType.Exile ? "제외" : "묘지", new Vector3(0, 0.002f, -0.95f),
                2f, new Color(1, 1, 1, 0.7f), 1.2f, 0.3f);
            view.Refresh(null, 0);
            return view;
        }

        /// <summary>맨 위 카드(마지막에 들어온 카드)와 장수. 연출로 카드가 막 도착했을 때도 부른다.</summary>
        public void Refresh(Entity top, int count)
        {
            _top.SetActive(top != null);
            _count.gameObject.SetActive(count > 0);
            _count.text = count.ToString();
            if (top == null)
                return;
            _topBody.sharedMaterial = MaterialCache.Get(CardPalette.Body(top.Type));
            _topName.text = top.Definition.Name ?? "";
        }
    }
}
