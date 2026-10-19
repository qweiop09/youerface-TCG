using System.Collections.Generic;
using TCG.Client.Cards;
using TCG.Client.Common;
using TCG.Client.Presentation;
using TCG.Core.Events;
using TCG.Core.State;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TCG.Client.Board
{
    /// <summary>
    /// 체인 표시 (GameDesign §11.3): 지금 체인에 쌓인 효과를 작은 카드로 체인 번호 순서대로 보여 준다 (화면 왼쪽 아래).
    /// 발동하면 오른쪽에 붙고, 처리 중인 카드는 노란 테두리, 무효면 어둡게 + "무효". 처리가 끝난 카드는 빠지고 체인이 끝나면 비운다.
    /// 마우스를 올리면 그 카드를 크게 키운다.
    /// </summary>
    public sealed class ChainStackView : MonoBehaviour
    {
        public const float CardScale = 0.26f;
        public const float HoverScale = 0.62f;
        const float Step = CardDetailView.Width * CardScale + 14f;
        const float RowWidth = 360f;
        const float RowHeight = CardDetailView.Height * CardScale + 44f;

        static readonly Color MineColor = new Color(0.35f, 0.6f, 1f);
        static readonly Color TheirsColor = new Color(1f, 0.45f, 0.35f);
        static readonly Color ResolvingColor = new Color(1f, 0.85f, 0.3f);

        sealed class Link
        {
            public int ChainNumber;
            public int SourceId;
            public RectTransform Root;
            public Image Outline;
            public TextMeshProUGUI Label;
            public CardDetailView Card;
            public Color Owner;
        }

        readonly List<Link> _links = new List<Link>();
        RectTransform _row;
        TextMeshProUGUI _title;

        /// <summary>지금 보이는 체인 번호 (테스트·확인용).</summary>
        public IEnumerable<int> ChainNumbers
        {
            get { foreach (var link in _links) yield return link.ChainNumber; }
        }
        public int Count => _links.Count;

        public static ChainStackView Create(Transform canvas)
        {
            var root = UiFactory.Rect(canvas, "ChainStack", new Vector2(0f, 0f), Vector2.zero, new Vector2(RowWidth, RowHeight + 30f));
            root.pivot = Vector2.zero;
            root.anchoredPosition = new Vector2(16f, 90f);
            var view = root.gameObject.AddComponent<ChainStackView>();
            view._title = UiFactory.Text(root, "Title", "", new Vector2(0f, 1f), new Vector2(90f, -14f), new Vector2(180f, 30f), 22,
                TextAlignmentOptions.Left);
            view._title.fontStyle = FontStyles.Bold;
            view._title.color = ResolvingColor;
            view._row = UiFactory.Rect(root, "Row", new Vector2(0f, 0f), Vector2.zero, new Vector2(RowWidth, RowHeight));
            view._row.pivot = Vector2.zero;
            return view;
        }

        public void Record(PresentationContext context, GameEvent e)
        {
            switch (e)
            {
                case EffectActivatedEvent activated:
                    Push(context, activated);
                    break;
                case EffectResolvingEvent resolving:
                    // 앞서 처리 중이던 효과는 끝났다.
                    RemoveWhere(link => link.ChainNumber > resolving.ChainNumber);
                    var current = FindLink(resolving.ChainNumber);
                    if (current != null)
                        current.Outline.color = ResolvingColor;
                    break;
                case EffectNegatedEvent negated:
                    RemoveWhere(link => link.ChainNumber > negated.ChainNumber);
                    var link2 = FindLink(negated.ChainNumber);
                    if (link2 != null)
                    {
                        link2.Outline.color = new Color(0.3f, 0.3f, 0.3f);
                        link2.Label.text = $"체인 {negated.ChainNumber} · 무효";
                        link2.Card.GetComponent<Image>().color *= 0.55f;
                    }
                    break;
                case ChainEndedEvent _:
                case GameEndedEvent _:
                    RemoveWhere(_ => true);
                    break;
            }
            _title.text = _links.Count > 0 ? "체인" : "";
        }

        void Push(PresentationContext context, EffectActivatedEvent e)
        {
            var entity = context.After.GetEntity(e.SourceId) ?? context.Before.GetEntity(e.SourceId);
            if (entity == null)
                return;
            // 같은 번호가 이미 있으면 (새 체인) 그 뒤를 지우고 다시 쌓는다.
            RemoveWhere(link => link.ChainNumber >= e.ChainNumber);

            var owner = context.IsMine(e.ControllerIndex) ? MineColor : TheirsColor;
            float w = CardDetailView.Width * CardScale, h = CardDetailView.Height * CardScale;
            var root = UiFactory.Rect(_row, "Link" + e.ChainNumber, new Vector2(0f, 0f), Vector2.zero, new Vector2(w + 8f, h + 36f));
            root.pivot = Vector2.zero;
            var outline = UiFactory.Panel(root, "Outline", new Vector2(0.5f, 0f), new Vector2(0f, h * 0.5f + 4f), new Vector2(w + 8f, h + 8f), owner);
            outline.raycastTarget = false;
            var label = UiFactory.Text(root, "Label", $"체인 {e.ChainNumber}", new Vector2(0.5f, 1f), new Vector2(0f, -12f), new Vector2(w + 30f, 24f), 18);
            label.color = owner;
            label.fontStyle = FontStyles.Bold;

            // 왼쪽 아래 기준으로 줄여 두어, 크게 볼 때 화면 안쪽(위·오른쪽)으로 커진다.
            var holder = UiFactory.Rect(root, "Holder", Vector2.zero, Vector2.zero, new Vector2(CardDetailView.Width, CardDetailView.Height));
            holder.pivot = Vector2.zero;
            holder.anchoredPosition = new Vector2(4f, 4f);
            holder.localScale = Vector3.one * CardScale;
            var card = CardDetailView.Create(holder);
            card.SideNotes = false;
            card.Show(entity);
            var hover = card.gameObject.AddComponent<ChainCardHover>();
            hover.Init(root, holder);

            _links.Add(new Link { ChainNumber = e.ChainNumber, SourceId = e.SourceId, Root = root, Outline = outline, Label = label, Card = card, Owner = owner });
            Arrange();
        }

        Link FindLink(int chainNumber)
        {
            foreach (var link in _links)
                if (link.ChainNumber == chainNumber)
                    return link;
            return null;
        }

        void RemoveWhere(System.Predicate<Link> match)
        {
            for (int i = _links.Count - 1; i >= 0; i--)
            {
                if (!match(_links[i]))
                    continue;
                Shapes.DestroyImmediateOrDeferred(_links[i].Root.gameObject);
                _links.RemoveAt(i);
            }
            Arrange();
        }

        /// <summary>체인 번호 순서로 왼쪽부터. 많으면 겹쳐서 폭 안에 넣는다.</summary>
        void Arrange()
        {
            float width = RowWidth;
            float w = CardDetailView.Width * CardScale + 8f;
            float step = _links.Count <= 1 ? Step : Mathf.Min(Step, (width - w) / (_links.Count - 1));
            for (int i = 0; i < _links.Count; i++)
            {
                _links[i].Root.anchoredPosition = new Vector2(step * i, 0f);
                _links[i].Root.SetSiblingIndex(i);
            }
        }

        /// <summary>마우스를 올리면 크게, 내리면 원래대로.</summary>
        sealed class ChainCardHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            RectTransform _root, _holder;
            int _sibling;

            public void Init(RectTransform root, RectTransform holder)
            {
                _root = root;
                _holder = holder;
            }

            public void OnPointerEnter(PointerEventData eventData)
            {
                _sibling = _root.GetSiblingIndex();
                _root.SetAsLastSibling();
                _holder.localScale = Vector3.one * HoverScale;
            }

            public void OnPointerExit(PointerEventData eventData)
            {
                _holder.localScale = Vector3.one * CardScale;
                if (_root != null && _root.parent != null && _sibling < _root.parent.childCount)
                    _root.SetSiblingIndex(_sibling);
            }
        }
    }
}
