using System.Collections.Generic;
using TCG.Client.Battle;
using TCG.Client.Common;
using TCG.Core.Commands;
using TCG.Core.State;
using UnityEngine;
using UnityEngine.UI;

namespace TCG.Client.Interaction
{
    /// <summary>
    /// 체인 기회 창 (GameDesign §7.2 규칙 3). 체인에는 한 번에 하나: 효과 하나를 누르면 바로 체인에 올라가고,
    /// 다음 체인 기회는 규칙대로 돌아온다.
    /// 올릴 수 있는 효과를 한 창에 모두 보여 주되, <b>강제</b>(무조건 사용) 효과와 <b>선택</b>(체인 걸 수 있는) 효과를
    /// 색이 다른 얇은 선 상자로 나눠 묶는다. 강제 효과가 남아 있으면 패스할 수 없다.
    /// 상자 영역이 <see cref="MaxListHeight"/>를 넘으면 창 높이는 고정하고 안쪽을 스크롤한다 (휠·끌기·스크롤바).
    /// </summary>
    public sealed class DecisionPanel : MonoBehaviour
    {
        public static readonly Color MandatoryLine = new Color(1f, 0.55f, 0.3f);
        public static readonly Color OptionalLine = new Color(0.4f, 0.78f, 1f);
        static readonly Color PanelColor = new Color(0.08f, 0.09f, 0.12f, 0.96f);
        static readonly Color RowColor = new Color(0.17f, 0.19f, 0.25f);

        const float RowHeight = 70f;
        const float RowGap = 12f;
        const float BoxPadding = 34f;
        const float BoxWidth = 900f;
        const float HeaderHeight = 130f;
        const float FooterHeight = 120f;
        /// <summary>상자 영역이 이보다 높으면 스크롤한다 (효과 약 5줄).</summary>
        public const float MaxListHeight = 440f;

        BattleSession _session;
        RectTransform _panel;
        TMPro.TextMeshProUGUI _title, _subtitle;
        Button _pass;
        ScrollRect _scroll;
        RectTransform _viewport, _list;
        readonly List<GameObject> _content = new List<GameObject>();
        PendingDecision _shownFor;

        public bool IsOpen => _panel != null && _panel.gameObject.activeSelf;
        public IReadOnlyList<int> Options => _shownFor?.Options ?? (IReadOnlyList<int>)new int[0];
        /// <summary>지금 창에 보이는 강제·선택 효과 수 (테스트·확인용).</summary>
        public int MandatoryShown { get; private set; }
        public int OptionalShown { get; private set; }
        /// <summary>효과가 많아 스크롤 중인가.</summary>
        public bool IsScrollable => _scroll != null && _scroll.vertical;

        /// <summary>스크롤 위치 (1 = 맨 위, 0 = 맨 아래).</summary>
        public void ScrollTo(float normalized)
        {
            if (_scroll != null)
                _scroll.verticalNormalizedPosition = Mathf.Clamp01(normalized);
        }

        public void Init(BattleSession session, Transform canvas)
        {
            _session = session;
            _panel = UiFactory.Panel(canvas, "DecisionPanel", new Vector2(0.5f, 0.5f), new Vector2(0, 40), new Vector2(980, 600), PanelColor).rectTransform;
            _title = UiFactory.Text(_panel, "Title", "", new Vector2(0.5f, 1f), new Vector2(0, -46), new Vector2(900, 60), 38);
            _subtitle = UiFactory.Text(_panel, "Subtitle", "", new Vector2(0.5f, 1f), new Vector2(0, -96), new Vector2(900, 44), 23);
            _subtitle.color = new Color(1f, 1f, 1f, 0.7f);
            _pass = UiFactory.Button(_panel, "Pass", "패스", new Vector2(0.5f, 0f), new Vector2(0, 62),
                new Vector2(300, 72), new Color(0.3f, 0.34f, 0.42f), Pass, 30f);
            BuildScrollArea();
            _panel.gameObject.SetActive(false);
        }

        /// <summary>상자들이 들어갈 스크롤 영역: 보이는 창(viewport, 잘라내기) + 내용(list) + 오른쪽 세로 스크롤바.</summary>
        void BuildScrollArea()
        {
            _viewport = UiFactory.Rect(_panel, "Viewport", new Vector2(0.5f, 1f), Vector2.zero, new Vector2(BoxWidth + 40f, MaxListHeight));
            _viewport.pivot = new Vector2(0.5f, 1f);
            _viewport.anchoredPosition = new Vector2(0, -HeaderHeight + 10f);
            _viewport.gameObject.AddComponent<RectMask2D>();
            // 빈 곳에서도 휠·끌기를 받도록 투명한 바탕
            _viewport.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0);

            _list = UiFactory.Rect(_viewport, "List", new Vector2(0.5f, 1f), Vector2.zero, new Vector2(BoxWidth + 40f, 0));
            _list.pivot = new Vector2(0.5f, 1f);

            var bar = UiFactory.Panel(_panel, "Scrollbar", new Vector2(1f, 1f), Vector2.zero, new Vector2(10f, MaxListHeight), new Color(1, 1, 1, 0.08f));
            bar.rectTransform.pivot = new Vector2(1f, 1f);
            bar.rectTransform.anchoredPosition = new Vector2(-14f, -HeaderHeight + 10f);
            var handle = UiFactory.Panel(bar.transform, "Handle", new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Color(1, 1, 1, 0.45f));
            handle.rectTransform.anchorMin = Vector2.zero;
            handle.rectTransform.anchorMax = Vector2.one;
            var scrollbar = bar.gameObject.AddComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.handleRect = handle.rectTransform;
            scrollbar.targetGraphic = handle;

            _scroll = _viewport.gameObject.AddComponent<ScrollRect>();
            _scroll.viewport = _viewport;
            _scroll.content = _list;
            _scroll.horizontal = false;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 40f;
            _scroll.verticalScrollbar = scrollbar;
            _scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        }

        void Update()
        {
            if (_session == null)
                return;
            var decision = _session.HumanHasDecision ? _session.State.Decision : null;
            if (decision == null)
            {
                if (_panel.gameObject.activeSelf)
                    _panel.gameObject.SetActive(false);
                _shownFor = null;
                return;
            }
            if (decision != _shownFor)
                Open(decision);
        }

        void Open(PendingDecision decision)
        {
            _shownFor = decision;
            foreach (var go in _content)
                Destroy(go);
            _content.Clear();
            _panel.gameObject.SetActive(true);

            var mandatory = new List<(int index, PendingTrigger trigger)>();
            var optional = new List<(int index, PendingTrigger trigger)>();
            for (int i = 0; i < decision.Options.Count; i++)
            {
                var t = _session.State.PendingTriggers.Find(p => p.Id == decision.Options[i]);
                if (t == null)
                    continue;
                (t.IsOptional ? optional : mandatory).Add((i, t));
            }
            MandatoryShown = mandatory.Count;
            OptionalShown = optional.Count;

            _title.text = $"체인 {_session.State.Chain.Count + 1}에 올릴 효과를 고르세요";
            _subtitle.text = decision.CanPass
                ? "하나만 올릴 수 있습니다. 올리지 않으려면 패스."
                : "하나만 올릴 수 있습니다. 무조건 사용 효과가 남아 있어 패스할 수 없습니다.";
            _pass.interactable = decision.CanPass;

            // 내용(list) 안에서 위에서부터: 강제 상자 → 선택 상자. 넘치면 창 높이를 고정하고 스크롤.
            float y = -20f;
            if (mandatory.Count > 0)
                y = Group(mandatory, "무조건 사용", MandatoryLine, y);
            if (optional.Count > 0)
                y = Group(optional, "체인 걸 수 있음", OptionalLine, y);

            float contentHeight = -y;
            float visible = Mathf.Min(contentHeight, MaxListHeight);
            _list.sizeDelta = new Vector2(_list.sizeDelta.x, contentHeight);
            _viewport.sizeDelta = new Vector2(_viewport.sizeDelta.x, visible);
            _scroll.verticalScrollbar.GetComponent<RectTransform>().sizeDelta = new Vector2(10f, visible);
            _scroll.vertical = contentHeight > MaxListHeight + 0.5f;
            _scroll.verticalScrollbar.gameObject.SetActive(_scroll.vertical);
            _list.anchoredPosition = Vector2.zero;
            _scroll.verticalNormalizedPosition = 1f;
            _panel.sizeDelta = new Vector2(980, HeaderHeight + visible + FooterHeight - 10f);
        }

        /// <summary>얇은 선 상자 하나에 효과 버튼들을 넣는다. 다음 상자를 놓을 y를 돌려준다.</summary>
        float Group(List<(int index, PendingTrigger trigger)> items, string label, Color line, float top)
        {
            float height = BoxPadding + items.Count * (RowHeight + RowGap) - RowGap + 18f;
            var box = UiFactory.OutlineBox(_list, $"Box {label}", new Vector2(0.5f, 1f), new Vector2(0, top - height * 0.5f),
                new Vector2(BoxWidth, height), line, label, PanelColor);
            _content.Add(box.gameObject);

            for (int k = 0; k < items.Count; k++)
            {
                int index = items[k].index;
                var row = UiFactory.Button(box, $"Option {items[k].trigger.Id}", Describe(items[k].trigger),
                    new Vector2(0.5f, 1f), new Vector2(0, -BoxPadding - RowHeight * 0.5f - k * (RowHeight + RowGap)),
                    new Vector2(BoxWidth - 40f, RowHeight), RowColor, () => ActivateOption(index), 25f);
                // 행 왼쪽에 상자 색 띠로 한 번 더 구분
                UiFactory.Panel(row.transform, "Stripe", new Vector2(0f, 0.5f), new Vector2(3f, 0), new Vector2(6f, RowHeight), line).raycastTarget = false;
            }
            return top - height - 26f;
        }

        string Describe(PendingTrigger trigger)
        {
            var source = _session.State.GetEntity(trigger.SourceId);
            var effect = Core.Effects.EffectLookup.Get(source, trigger.EffectIndex, _session.Engine.Registry);
            return $"{source?.Definition.Name} — {EffectText.Describe(effect)}";
        }

        /// <summary>선택지 index번 효과를 체인에 올린다.</summary>
        public void ActivateOption(int index)
        {
            var decision = _session.State.Decision;
            if (decision == null || index < 0 || index >= decision.Options.Count)
                return;
            Submit(new ChainActivateCommand(_session.Human, decision.Options[index]));
        }

        public void Pass() => Submit(new PassChainCommand(_session.Human));

        void Submit(Command command)
        {
            if (_session.TrySubmit(command, out var reason))
            {
                _panel.gameObject.SetActive(false);
                _shownFor = null;
            }
            else
            {
                _subtitle.text = reason;
            }
        }
    }
}
