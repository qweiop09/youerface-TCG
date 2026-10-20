using TCG.Client.Cards;
using TCG.Client.Common;
using TMPro;
using UnityEngine;

namespace TCG.Client.Board
{
    /// <summary>덱 더미: 장수만큼 두께가 있는 카드 뒷면 + 장수.</summary>
    public sealed class PileView : MonoBehaviour
    {
        Transform _stack;
        TextMeshPro _count, _info;

        public static PileView Create(Transform parent, Vector3 position)
        {
            var go = new GameObject("Deck");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            var view = go.AddComponent<PileView>();

            view._stack = Shapes.Box(go.transform, "Stack", Vector3.zero, new Vector3(CardView.Width, 0.01f, CardView.Height), CardPalette.Back).transform;
            view._count = Shapes.Label(go.transform, "Count", "", Vector3.zero, 3f, CardPalette.BackTrim, 1f, 0.5f);
            view._info = Shapes.Label(go.transform, "Info", "", new Vector3(0, 0.01f, -1.0f), 2f, new Color(1, 1, 1, 0.75f), 2.2f, 0.3f);
            return view;
        }

        public void Refresh(int deck, int graveyard, int exile)
        {
            float height = Mathf.Max(0.01f, deck * 0.012f);
            _stack.localScale = new Vector3(CardView.Width, height, CardView.Height);
            _stack.localPosition = new Vector3(0, height * 0.5f, 0);
            _stack.gameObject.SetActive(deck > 0);
            _count.transform.localPosition = new Vector3(0, height + 0.01f, 0);
            _count.text = deck.ToString();
            _info.text = "덱"; // 묘지·제외는 옆의 GraveView가 따로 보여 준다
        }
    }
}
