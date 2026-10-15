using System;
using System.Collections.Generic;
using TCG.Client.Battle;
using TCG.Client.CardMaker;
using TCG.Client.Cards;
using TCG.Client.Common;
using TCG.Core.Data;
using TCG.Core.Scoring;
using TCG.Core.Serialization;
using TCG.Core.State;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TCG.Client.DeckBuilder
{
    /// <summary>
    /// 덱 편집 화면 (DeckBuilder 장면, M5-2·M5-3). 왼쪽: 카드 목록(출처·종류·코스트·점수 필터, 정렬),
    /// 가운데: 카드 미리보기·코스트 분포·점수 합계, 오른쪽: 덱 이름·검사 결과·덱 카드.
    /// 점수 필터는 목록을 거르는 화면 기능일 뿐 덱 제한이 아니다(GameDesign §6). 규칙 검사는 Core <see cref="DeckValidator"/>.
    /// </summary>
    public sealed class DeckBuilderScreen : MonoBehaviour
    {
        public const string SceneName = "DeckBuilder";

        public enum SourceFilter { All, Mine, Sample }
        public enum SortMode { Cost, ScoreHigh, ScoreLow, Name, Recent }

        // 화면 상수 (규칙값 아님)
        const int MaxNameLength = 20;
        const float CardRowHeight = 62f;
        const float DeckRowHeight = 54f;
        static readonly CardType?[] TypeOptions = { null, CardType.Minion, CardType.Spell, CardType.Field, CardType.Companion };
        static readonly int?[] CostOptions = { null, 0, 1, 2, 3, 4, 5, 6, DeckReport.CostCurveBuckets - 1 };
        static readonly int?[] ScoreOptions = { null, 0, 10, 20, 30, 40, 50, 60, 80, 100 };
        static readonly SourceFilter[] SourceOptions = { SourceFilter.All, SourceFilter.Mine, SourceFilter.Sample };
        static readonly SortMode[] SortOptions = { SortMode.Cost, SortMode.ScoreHigh, SortMode.ScoreLow, SortMode.Name, SortMode.Recent };

        static readonly Color Background = new Color(0.06f, 0.07f, 0.09f);
        static readonly Color BarColor = new Color(0.3f, 0.3f, 0.35f);
        static readonly Color RowColor = new Color(0.13f, 0.15f, 0.19f);
        static readonly Color RowPicked = new Color(0.2f, 0.27f, 0.38f);
        static readonly Color SmallButton = new Color(0.24f, 0.27f, 0.34f);
        static readonly Color ErrorColor = new Color(1f, 0.5f, 0.45f);
        static readonly Color OkColor = new Color(0.6f, 0.9f, 0.6f);
        static readonly Color MutedColor = new Color(0.75f, 0.77f, 0.82f);
        static readonly Color BarFill = new Color(0.35f, 0.6f, 0.9f);

        readonly RuleConfig _rules = RuleConfig.CreateDefault();
        readonly ScoreTable _scoreTable = ScoreTable.CreateDefault();
        readonly Dictionary<string, int> _scores = new Dictionary<string, int>();
        /// <summary>카드 ID → 검색 대상 글(소문자).</summary>
        readonly Dictionary<string, string> _searchText = new Dictionary<string, string>();
        readonly Image[] _curveBars = new Image[DeckReport.CostCurveBuckets];
        readonly TextMeshProUGUI[] _curveCounts = new TextMeshProUGUI[DeckReport.CostCurveBuckets];

        Canvas _canvas;
        RectTransform _filterBar, _filterCells;
        TMP_InputField _search;
        FormColumn _cardList, _deckList, _libraryList;
        GameObject _libraryPanel;
        CardDetailView _preview;
        TextMeshProUGUI _heading, _types, _scoreSummary, _status, _visibleCount;
        string _savedJson;
        string _pendingDelete;

        public LocalCardLibrary Cards { get; set; }
        public LocalDeckLibrary Decks { get; set; }
        public CardPool Pool { get; private set; }
        public DeckDefinition Deck { get; private set; }
        public DeckReport Report { get; private set; }
        public string PreviewId { get; private set; }
        public List<CardDefinition> VisibleCards { get; } = new List<CardDefinition>();

        public SourceFilter Source { get; private set; } = SourceFilter.All;
        public CardType? TypeFilter { get; private set; }
        /// <summary>코스트 필터. <see cref="DeckReport.CostCurveBuckets"/>-1이면 "그 이상".</summary>
        public int? CostFilter { get; private set; }
        /// <summary>점수 N 이하만 (카드 실제 점수 기준).</summary>
        public int? ScoreMax { get; private set; }
        public SortMode Sort { get; private set; } = SortMode.Cost;
        /// <summary>
        /// 검색어. 띄어쓰기로 나눈 낱말이 전부 맞아야 한다(AND). 숫자만("3")이나 "3코"·"3코스트"·"코스트3"은 코스트,
        /// 종류 이름과 똑같은 낱말("기물"·"주문"·"필드"·"동료")은 종류,
        /// 나머지는 이름·키워드·효과 문구(소환물 포함)에 들어 있는지 (대소문자 무시). 비면 전부.
        /// </summary>
        public string Search { get; private set; } = "";

        public string StatusText => _status != null ? _status.text : "";
        public bool IsDirty => Deck != null && DeckSerializer.Serialize(Deck, false) != _savedJson;
        public bool LibraryOpen => _libraryPanel != null && _libraryPanel.activeSelf;

        void Start()
        {
            UiFactory.EnsureEventSystem();
            Cards = Cards ?? new LocalCardLibrary();
            Decks = Decks ?? new LocalDeckLibrary();
            Build();
            ReloadPool();
            NewDeck();
        }

        void Build()
        {
            _canvas = UiFactory.OverlayCanvas(transform, "DeckBuilder UI");
            var root = _canvas.transform;
            UiFactory.Stretch(root, "Background").gameObject.AddComponent<Image>().color = Background;

            UiFactory.Button(root, "Back", "← 시작 화면", new Vector2(0f, 1f), new Vector2(140, -50), new Vector2(240, 70), BarColor,
                () => SceneManager.LoadScene("Title"), 28);
            UiFactory.Text(root, "Title", "덱 편집", new Vector2(0.5f, 1f), new Vector2(80, -50), new Vector2(500, 70), 44).fontStyle = FontStyles.Bold;
            UiFactory.Button(root, "New", "새 덱", new Vector2(1f, 1f), new Vector2(-110, -50), new Vector2(180, 70), BarColor, NewDeck, 28);
            UiFactory.Button(root, "Library", "내 덱", new Vector2(1f, 1f), new Vector2(-300, -50), new Vector2(180, 70), BarColor, OpenLibrary, 28);
            UiFactory.Button(root, "Duplicate", "복제", new Vector2(1f, 1f), new Vector2(-490, -50), new Vector2(180, 70), BarColor, Duplicate, 28);
            UiFactory.Button(root, "Save", "저장", new Vector2(1f, 1f), new Vector2(-680, -50), new Vector2(180, 70), new Color(0.2f, 0.5f, 0.3f), Save, 28);

            // 왼쪽: 필터 + 카드 목록
            _filterBar = UiFactory.Rect(root, "Filters", new Vector2(0f, 1f), new Vector2(410, -170), new Vector2(760, 120));
            _filterCells = UiFactory.Stretch(_filterBar, "Cells");
            // 검색 칸은 다시 쌓지 않는다 (입력 중 포커스 유지)
            _search = UiFactory.InputField(_filterBar, "Search", new Vector2(0f, 1f), new Vector2(253f * 2 + 85f, -88), new Vector2(170, 50), 22);
            ((TextMeshProUGUI)_search.placeholder).text = "이름·효과·3코";
            _search.characterLimit = MaxNameLength;
            _search.onValueChanged.AddListener(SetSearch);
            _visibleCount = UiFactory.Text(_filterBar, "Visible", "", new Vector2(0f, 1f), new Vector2(253f * 2 + 210f, -88), new Vector2(76, 50), 20);
            _visibleCount.color = MutedColor;
            _cardList = FormColumn.Create(root, "CardList", new Vector2(0f, 0.5f), new Vector2(410, -95), new Vector2(760, 780));

            // 가운데: 미리보기 + 덱 요약
            // 카드 왼쪽 위 키워드 설명 창·소환물 미니 카드 자리를 남기도록 0.7배, 오른쪽으로 옮김 (카드 목록과 덱 칸 사이).
            var previewRoot = UiFactory.Rect(root, "Preview", new Vector2(0.5f, 0.5f), new Vector2(181, 180),
                new Vector2(CardDetailView.Width, CardDetailView.Height));
            previewRoot.localScale = Vector3.one * 0.7f;
            _preview = CardDetailView.Create(previewRoot);
            BuildCurve(root);
            _types = UiFactory.Text(root, "Types", "", new Vector2(0.5f, 0.5f), new Vector2(80, -380), new Vector2(480, 40), 24);
            _types.color = MutedColor;
            _scoreSummary = UiFactory.Text(root, "ScoreSummary", "", new Vector2(0.5f, 0.5f), new Vector2(80, -420), new Vector2(480, 40), 26);

            // 오른쪽: 덱
            _deckList = FormColumn.Create(root, "DeckList", new Vector2(1f, 0.5f), new Vector2(-330, -60), new Vector2(600, 900));

            _status = UiFactory.Text(root, "Status", "", new Vector2(0.5f, 0f), new Vector2(0, 28), new Vector2(1600, 40), 24);
            _status.color = new Color(1f, 0.85f, 0.5f);

            BuildLibraryPanel(root);
        }

        void BuildCurve(Transform root)
        {
            var box = UiFactory.Rect(root, "CostCurve", new Vector2(0.5f, 0.5f), new Vector2(80, -240), new Vector2(480, 220));
            UiFactory.Text(box, "Title", "코스트 분포", new Vector2(0.5f, 1f), new Vector2(0, -16), new Vector2(480, 32), 24).color = MutedColor;
            const float slot = 56f;
            for (int i = 0; i < DeckReport.CostCurveBuckets; i++)
            {
                float x = (i - (DeckReport.CostCurveBuckets - 1) * 0.5f) * slot;
                var bar = UiFactory.Panel(box, "Bar" + i, new Vector2(0.5f, 0f), new Vector2(x, 36), new Vector2(slot - 14f, 0), BarFill);
                bar.rectTransform.pivot = new Vector2(0.5f, 0f);
                _curveBars[i] = bar;
                _curveCounts[i] = UiFactory.Text(box, "Count" + i, "", new Vector2(0.5f, 0f), new Vector2(x, 50), new Vector2(slot, 28), 20);
                string label = i == DeckReport.CostCurveBuckets - 1 ? i + "+" : i.ToString();
                UiFactory.Text(box, "Label" + i, label, new Vector2(0.5f, 0f), new Vector2(x, 16), new Vector2(slot, 28), 22).color = MutedColor;
            }
        }

        // ------------------------------------------------------------------ 카드 모음

        /// <summary>내 카드·샘플 카드를 다시 읽는다 (카드 보관함을 바꾼 뒤에도 부른다).</summary>
        public void ReloadPool()
        {
            Pool = CardPool.Load(Cards);
            _scores.Clear();
            _searchText.Clear();
            foreach (var card in Pool.Cards)
            {
                _scores[card.Id] = ScoreCalculator.Calculate(card, _scoreTable);
                _searchText[card.Id] = SearchTextOf(card);
            }
            if (Deck != null)
                Refresh();
        }

        public int ScoreOf(string id) => _scores.TryGetValue(id, out int score) ? score : 0;

        // ------------------------------------------------------------------ 덱 편집

        public void NewDeck()
        {
            Open(new DeckDefinition { Id = NewId(), Name = "새 덱" });
            _savedJson = null;
            Refresh();
            SetStatus("");
        }

        /// <summary>덱을 편집기에 연다(복사본을 편집).</summary>
        public void Open(DeckDefinition deck)
        {
            Deck = Clone(deck);
            _savedJson = DeckSerializer.Serialize(Deck, false);
            PreviewId = Deck.Cards.Count > 0 ? Deck.Cards[0] : null;
            _cardList.Content.anchoredPosition = Vector2.zero;
            _deckList.Content.anchoredPosition = Vector2.zero;
            Refresh();
        }

        /// <summary>한 장 넣는다. 규칙에 걸리면 넣지 않고 사유를 상태 줄에.</summary>
        public bool AddCard(string id)
        {
            PreviewId = id;
            if (!Pool.Catalog.TryGet(id, out var card))
            {
                SetStatus("없는 카드입니다.");
                Refresh();
                return false;
            }
            if (!DeckValidator.CanAdd(Deck, card, _rules, out string reason))
            {
                SetStatus(reason);
                Refresh();
                return false;
            }
            DeckEditing.Add(Deck, id);
            SetStatus("");
            Refresh();
            return true;
        }

        public bool RemoveCard(string id)
        {
            bool removed = DeckEditing.RemoveOne(Deck, id);
            PreviewId = id;
            SetStatus("");
            Refresh();
            return removed;
        }

        public void ShowPreview(string id)
        {
            PreviewId = id;
            RefreshPreview();
        }

        public void SetSource(SourceFilter value) { Source = value; Refresh(); }
        public void SetTypeFilter(CardType? value) { TypeFilter = value; Refresh(); }
        public void SetCostFilter(int? value) { CostFilter = value; Refresh(); }
        public void SetScoreMax(int? value) { ScoreMax = value; Refresh(); }
        public void SetSort(SortMode value) { Sort = value; Refresh(); }

        /// <summary>검색어가 바뀌면 카드 목록만 다시 쌓는다.</summary>
        public void SetSearch(string value)
        {
            Search = value ?? "";
            if (_search.text != Search)
                _search.SetTextWithoutNotify(Search);
            BuildCardList();
        }

        public void SetName(string name)
        {
            Deck.Name = name;
            RefreshHeading();
        }

        static string NewId() => "d_" + Guid.NewGuid().ToString("N").Substring(0, 12);

        static DeckDefinition Clone(DeckDefinition deck) => DeckSerializer.Deserialize(DeckSerializer.Serialize(deck, false));

        void SetStatus(string text) => _status.text = text;

        // ------------------------------------------------------------------ 다시 그리기

        void Refresh()
        {
            Report = DeckValidator.Check(Deck, Pool.Catalog, _rules, _scoreTable);
            BuildFilters();
            BuildCardList();
            BuildDeckList();
            RefreshSummary();
            RefreshPreview();
        }

        void BuildFilters()
        {
            for (int i = _filterCells.childCount - 1; i >= 0; i--)
                Destroy(_filterCells.GetChild(i).gameObject);
            FilterCycle(0, 0, "출처", SourceName(Source), d => SetSource(Step(SourceOptions, Source, d)));
            FilterCycle(1, 0, "종류", TypeFilter.HasValue ? KoreanTerms.Of(TypeFilter.Value) : "전체", d => SetTypeFilter(Step(TypeOptions, TypeFilter, d)));
            FilterCycle(2, 0, "코스트", CostName(CostFilter), d => SetCostFilter(Step(CostOptions, CostFilter, d)));
            FilterCycle(0, 1, "점수", ScoreMax.HasValue ? $"{ScoreMax} 이하" : "전체", d => SetScoreMax(Step(ScoreOptions, ScoreMax, d)));
            FilterCycle(1, 1, "정렬", SortName(Sort), d => SetSort(Step(SortOptions, Sort, d)));
        }

        /// <summary>필터 칸: ◀ 이름: 값 ▶. 3칸 × 2줄.</summary>
        void FilterCycle(int column, int row, string label, string value, Action<int> step)
        {
            const float w = 246f, h = 50f;
            var cell = UiFactory.Rect(_filterCells, "Filter " + label, new Vector2(0f, 1f), new Vector2(253f * column + w * 0.5f, -32 - row * 56f), new Vector2(w, h));
            UiFactory.Panel(cell, "Back", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(w, h), RowColor).raycastTarget = false;
            UiFactory.Button(cell, "Prev", "◀", new Vector2(0f, 0.5f), new Vector2(22, 0), new Vector2(40, h - 6), SmallButton, () => step(-1), 22);
            UiFactory.Text(cell, "Value", $"{label}: {value}", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(w - 90, h), 22);
            UiFactory.Button(cell, "Next", "▶", new Vector2(1f, 0.5f), new Vector2(-22, 0), new Vector2(40, h - 6), SmallButton, () => step(1), 22);
        }

        /// <summary>필터·정렬을 적용한 카드 목록을 다시 계산한다.</summary>
        void ComputeVisible()
        {
            VisibleCards.Clear();
            foreach (var card in Pool.Cards)
            {
                bool sample = Pool.IsSample(card.Id);
                if (Source == SourceFilter.Mine && sample || Source == SourceFilter.Sample && !sample)
                    continue;
                if (TypeFilter.HasValue && card.Type != TypeFilter.Value)
                    continue;
                if (CostFilter.HasValue)
                {
                    int last = DeckReport.CostCurveBuckets - 1;
                    if (CostFilter.Value == last ? card.Cost < last : card.Cost != CostFilter.Value)
                        continue;
                }
                if (ScoreMax.HasValue && ScoreOf(card.Id) > ScoreMax.Value)
                    continue;
                if (!MatchesSearch(card))
                    continue;
                VisibleCards.Add(card);
            }
            VisibleCards.Sort(Compare);
        }

        bool MatchesSearch(CardDefinition card)
        {
            var terms = Search.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (terms.Length == 0)
                return true;
            _searchText.TryGetValue(card.Id, out string text);
            foreach (var term in terms)
            {
                if (TryParseCost(term, out int cost))
                {
                    if (card.Cost != cost)
                        return false;
                }
                else if (TryParseType(term, out var type))
                {
                    if (card.Type != type)
                        return false;
                }
                else if (text == null || !text.Contains(term.ToLowerInvariant()))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>종류 이름과 똑같은 낱말("기물", "주문" …)은 종류로 찾는다 (효과 문구의 "적 기물" 등과 섞이지 않게).</summary>
        static bool TryParseType(string term, out CardType type)
        {
            foreach (CardType t in Enum.GetValues(typeof(CardType)))
            {
                if (KoreanTerms.Of(t) == term)
                {
                    type = t;
                    return true;
                }
            }
            type = default;
            return false;
        }

        /// <summary>"3", "3코", "3코스트", "코스트3" → 3.</summary>
        static bool TryParseCost(string term, out int cost)
        {
            string digits = term;
            if (digits.StartsWith("코스트")) digits = digits.Substring(3);
            else if (digits.EndsWith("코스트")) digits = digits.Substring(0, digits.Length - 3);
            else if (digits.EndsWith("코")) digits = digits.Substring(0, digits.Length - 1);
            return int.TryParse(digits, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out cost);
        }

        /// <summary>이름·종류·키워드·효과 문구. 소환물 생성 효과는 소환물의 이름·효과도 넣는다.</summary>
        static string SearchTextOf(CardDefinition card)
        {
            var sb = new System.Text.StringBuilder();
            AppendSearchText(sb, card);
            return sb.ToString().ToLowerInvariant();
        }

        static void AppendSearchText(System.Text.StringBuilder sb, CardDefinition card)
        {
            sb.Append(card.Name).Append('\n').Append(KoreanTerms.Of(card.Type)).Append('\n');
            if (card.Keywords != null)
            {
                foreach (var keyword in card.Keywords)
                    sb.Append(KoreanTerms.Of(keyword)).Append('\n');
            }
            if (card.Effects == null)
                return;
            foreach (var effect in card.Effects)
            {
                sb.Append(EffectText.Describe(effect)).Append('\n');
                if (effect.Action?.Summon != null)
                    AppendSearchText(sb, effect.Action.Summon);
            }
        }

        int Compare(CardDefinition a, CardDefinition b)
        {
            int c = 0;
            switch (Sort)
            {
                case SortMode.ScoreHigh: c = ScoreOf(b.Id).CompareTo(ScoreOf(a.Id)); break;
                case SortMode.ScoreLow: c = ScoreOf(a.Id).CompareTo(ScoreOf(b.Id)); break;
                case SortMode.Name: c = string.CompareOrdinal(a.Name ?? "", b.Name ?? ""); break;
                case SortMode.Recent:
                    bool ha = Pool.Modified.TryGetValue(a.Id, out var ma);
                    bool hb = Pool.Modified.TryGetValue(b.Id, out var mb);
                    c = ha != hb ? (ha ? -1 : 1) : (ha ? mb.CompareTo(ma) : 0);
                    break;
            }
            if (c == 0) c = a.Cost.CompareTo(b.Cost);
            if (c == 0) c = string.CompareOrdinal(a.Name ?? "", b.Name ?? "");
            if (c == 0) c = string.CompareOrdinal(a.Id, b.Id);
            return c;
        }

        void BuildCardList()
        {
            ComputeVisible();
            _visibleCount.text = $"{VisibleCards.Count}장";
            var f = _cardList;
            f.Clear();
            if (VisibleCards.Count == 0)
                f.Note(Pool.Cards.Count == 0 ? "카드가 없습니다. 카드 제작에서 만들어 주세요." : "조건에 맞는 카드가 없습니다.", MutedColor, 26);
            foreach (var card in VisibleCards)
            {
                var c = card;
                int copies = DeckEditing.Count(Deck, c.Id);
                bool canAdd = DeckValidator.CanAdd(Deck, c, _rules, out _);
                bool broken = CardValidator.Validate(c).Count > 0;
                var row = CardRow(f, c, CardRowHeight, () => ShowPreview(c.Id));

                string info = $"{KoreanTerms.Of(c.Type)} · 점수 {ScoreOf(c.Id)}" + (Pool.IsSample(c.Id) ? " · 샘플" : "");
                var infoText = UiFactory.Text(row, "Info", broken ? "규칙 위반" : info, new Vector2(1f, 0.5f), new Vector2(-230, 0), new Vector2(240, CardRowHeight), 22,
                    TextAlignmentOptions.Right);
                infoText.color = broken ? ErrorColor : MutedColor;
                UiFactory.Text(row, "Copies", $"{copies}/{_rules.MaxCopiesPerCard}", new Vector2(1f, 0.5f), new Vector2(-82, 0), new Vector2(60, CardRowHeight), 24)
                    .color = copies > 0 ? Color.white : MutedColor;
                SetEnabled(UiFactory.Button(row, "Add", "+", new Vector2(1f, 0.5f), new Vector2(-28, 0), new Vector2(48, CardRowHeight - 12), SmallButton,
                    () => AddCard(c.Id), 30), canAdd);
            }
            f.Finish();
        }

        static void SetEnabled(Button button, bool enabled)
        {
            button.interactable = enabled;
            button.GetComponentInChildren<TextMeshProUGUI>().alpha = enabled ? 1f : 0.3f;
        }

        /// <summary>카드 한 줄: 누르면 미리보기. 왼쪽 코스트 원 + 이름.</summary>
        RectTransform CardRow(FormColumn f, CardDefinition card, float height, Action onClick)
        {
            var row = f.Block("Card " + card.Id, height);
            var back = UiFactory.Button(row, "Pick", "", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(f.Width, height),
                card.Id == PreviewId ? RowPicked : RowColor, onClick);
            UiFactory.Panel(back.transform, "TypeStripe", new Vector2(0f, 0.5f), new Vector2(3, 0), new Vector2(6, height), CardPalette.Body(card.Type))
                .raycastTarget = false;
            var cost = UiFactory.Badge(row, "Cost", new Vector2(0f, 0.5f), new Vector2(36, 0), height - 16, CardPalette.CostBadge, 24);
            cost.text = card.Cost.ToString();
            cost.transform.parent.GetComponent<Image>().raycastTarget = false;
            UiFactory.Text(row, "Name", string.IsNullOrEmpty(card.Name) ? "(이름 없음)" : card.Name, new Vector2(0f, 0.5f), new Vector2(70 + 150, 0),
                new Vector2(300, height), 26, TextAlignmentOptions.Left).textWrappingMode = TextWrappingModes.NoWrap;
            return row;
        }

        void BuildDeckList()
        {
            var f = _deckList;
            f.Clear();
            f.Input("덱 이름", Deck.Name, MaxNameLength, SetName);
            f.Header($"덱 {Deck.Cards.Count} / {_rules.DeckSize}장");
            if (Report.IsValid)
                f.Note("규칙 검사 통과 — 대전에 쓸 수 있습니다.", OkColor);
            else
                f.Note("· " + string.Join("\n· ", Report.Messages()), ErrorColor, 22);
            int missing = MissingCount();
            if (missing > 0)
                f.Buttons("", ($"없는 카드 {missing}장 빼기", () => RemoveMissingCards()));
            f.Space(6f);

            // 보여 주는 순서만 코스트순 (저장 순서는 넣은 순서 그대로)
            var sorted = new DeckDefinition { Cards = new List<string>(Deck.Cards) };
            DeckEditing.Sort(sorted, Pool.Catalog);
            foreach (var entry in DeckEditing.Distinct(sorted))
            {
                string id = entry.Id;
                if (!Pool.Catalog.TryGet(id, out var card))
                    card = new CardDefinition { Id = id, Name = "(없는 카드) " + id, Type = CardType.Spell };
                var row = CardRow(f, card, DeckRowHeight, () => ShowPreview(id));
                var copies = UiFactory.Text(row, "Copies", "×" + entry.Copies, new Vector2(1f, 0.5f), new Vector2(-150, 0), new Vector2(70, DeckRowHeight), 26);
                copies.color = Report.HasIssue(id) ? ErrorColor : Color.white;
                UiFactory.Button(row, "Remove", "−", new Vector2(1f, 0.5f), new Vector2(-82, 0), new Vector2(46, DeckRowHeight - 12), SmallButton,
                    () => RemoveCard(id), 30);
                SetEnabled(UiFactory.Button(row, "Add", "+", new Vector2(1f, 0.5f), new Vector2(-28, 0), new Vector2(46, DeckRowHeight - 12), SmallButton,
                    () => AddCard(id), 30), DeckValidator.CanAdd(Deck, card, _rules, out _));
            }
            if (Deck.Cards.Count == 0)
                f.Note("왼쪽 목록에서 + 로 카드를 넣으세요.", MutedColor);
            f.Finish();
            RefreshHeading();
        }

        int MissingCount()
        {
            int n = 0;
            foreach (var id in Deck.Cards)
            {
                if (!Pool.Catalog.TryGet(id, out _))
                    n++;
            }
            return n;
        }

        void RefreshSummary()
        {
            int max = 1;
            for (int i = 0; i < Report.CostCurve.Length; i++)
                max = Mathf.Max(max, Report.CostCurve[i]);
            for (int i = 0; i < Report.CostCurve.Length; i++)
            {
                int n = Report.CostCurve[i];
                _curveBars[i].rectTransform.sizeDelta = new Vector2(_curveBars[i].rectTransform.sizeDelta.x, 120f * n / max);
                _curveCounts[i].text = n > 0 ? n.ToString() : "";
                _curveCounts[i].rectTransform.anchoredPosition = new Vector2(_curveCounts[i].rectTransform.anchoredPosition.x, 36 + 120f * n / max + 14);
            }
            _types.text = $"기물 {Report.Count(CardType.Minion)} · 주문 {Report.Count(CardType.Spell)} · 필드 {Report.Count(CardType.Field)} · 동료 {Report.Count(CardType.Companion)}";
            int known = Report.CardCount;
            string average = known > 0 ? (Report.TotalScore / known).ToString() : "-";
            _scoreSummary.text = $"점수 합계 {Report.TotalScore} · 평균 {average} · 최고 {Report.MaxScore}";
        }

        void RefreshPreview()
        {
            CardDefinition card = null;
            if (PreviewId != null)
                Pool.Catalog.TryGet(PreviewId, out card);
            _preview.gameObject.SetActive(card != null);
            if (card != null)
                _preview.Show(new Entity(0, 0, card));
        }

        void RefreshHeading()
        {
            if (_heading == null)
            {
                _heading = UiFactory.Text(_canvas.transform, "Heading", "", new Vector2(0.5f, 1f), new Vector2(80, -100), new Vector2(500, 34), 24);
                _heading.color = MutedColor;
            }
            _heading.text = (string.IsNullOrEmpty(Deck.Name) ? "(이름 없음)" : Deck.Name) + (IsDirty ? "   — 저장 안 됨" : "");
        }

        // ------------------------------------------------------------------ 저장·내 덱

        public void Save()
        {
            if (string.IsNullOrWhiteSpace(Deck.Name))
                Deck.Name = "새 덱";
            Decks.Save(Deck);
            _savedJson = DeckSerializer.Serialize(Deck, false);
            SetStatus(Report.IsValid ? "저장했습니다." : "저장했습니다. 규칙 검사에 걸린 곳이 있어 대전에는 아직 쓸 수 없습니다.");
            Refresh();
            if (LibraryOpen)
                RefreshLibrary();
        }

        void BuildLibraryPanel(Transform root)
        {
            var panel = UiFactory.Stretch(root, "LibraryPanel");
            panel.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.8f);
            UiFactory.Text(panel, "Title", "내 덱", new Vector2(0.5f, 1f), new Vector2(0, -90), new Vector2(800, 70), 44).fontStyle = FontStyles.Bold;
            UiFactory.Button(panel, "Close", "닫기", new Vector2(0.5f, 0f), new Vector2(0, 80), new Vector2(240, 70), BarColor, CloseLibrary, 28);
            _libraryList = FormColumn.Create(panel, "List", new Vector2(0.5f, 0.5f), new Vector2(0, 10), new Vector2(1000, 760));
            _libraryPanel = panel.gameObject;
            _libraryPanel.SetActive(false);
        }

        public void OpenLibrary()
        {
            _pendingDelete = null;
            _libraryPanel.SetActive(true);
            _libraryPanel.transform.SetAsLastSibling();
            RefreshLibrary();
        }

        public void CloseLibrary() => _libraryPanel.SetActive(false);

        void RefreshLibrary()
        {
            var f = _libraryList;
            f.Clear();
            var entries = Decks.List();
            if (entries.Count == 0)
                f.Note("저장한 덱이 없습니다.", MutedColor, 28);
            foreach (var entry in entries)
            {
                var e = entry;
                var report = DeckValidator.Check(e.Deck, Pool.Catalog, _rules, _scoreTable);
                string state = report.IsValid ? "<color=#99E699>대전 가능</color>" : $"<color=#FF8073>문제 {report.Issues.Count}개</color>";
                f.Note($"{e.Deck.Name}  ·  {e.Deck.Cards.Count}장  ·  {state}", Color.white, 28);
                bool confirming = _pendingDelete == e.Id;
                f.Buttons("",
                    ("열기", () => OpenFromLibrary(e.Id)),
                    (confirming ? "정말 삭제" : "삭제", () => DeleteFromLibrary(e.Id)));
                f.Space(10f);
            }

            f.Header("샘플 덱");
            var samples = SampleData.LoadDecks();
            for (int i = 0; i < samples.Count; i++)
            {
                int index = i;
                f.Note($"{samples[i].Name}  ·  {samples[i].Cards.Count}장", Color.white, 28);
                f.Buttons("", ("복사해서 열기", () => CopySampleDeck(index)));
                f.Space(10f);
            }
            f.Finish();
        }

        public void OpenFromLibrary(string id)
        {
            var deck = Decks.Load(id);
            if (deck == null)
            {
                SetStatus("덱을 찾을 수 없습니다.");
                return;
            }
            Open(deck);
            CloseLibrary();
            SetStatus($"'{deck.Name}'을(를) 열었습니다.");
        }

        /// <summary>두 번 눌러야 지운다 (첫 번째는 확인 표시).</summary>
        public void DeleteFromLibrary(string id)
        {
            if (_pendingDelete != id)
            {
                _pendingDelete = id;
                RefreshLibrary();
                return;
            }
            Decks.Delete(id);
            _pendingDelete = null;
            if (Deck != null && Deck.Id == id)
                _savedJson = null;
            RefreshLibrary();
            RefreshHeading();
            SetStatus("덱을 지웠습니다.");
        }

        /// <summary>지금 덱을 새 ID로 복사해 연다(저장은 따로).</summary>
        public void Duplicate()
        {
            var copy = Clone(Deck);
            copy.Id = NewId();
            copy.Name = TrimName((copy.Name ?? "") + " 복사본");
            Open(copy);
            _savedJson = null;
            Refresh();
            SetStatus("복사본을 만들었습니다. 저장해야 보관됩니다.");
        }

        /// <summary>카드 모음에 없는 카드(지운 카드)를 덱에서 전부 뺀다.</summary>
        public int RemoveMissingCards()
        {
            int removed = DeckEditing.RemoveMissing(Deck, Pool.Catalog);
            SetStatus(removed > 0 ? $"없는 카드 {removed}장을 뺐습니다." : "");
            Refresh();
            return removed;
        }

        /// <summary>샘플 덱을 새 ID로 복사해 연다(저장은 따로).</summary>
        public void CopySampleDeck(int index)
        {
            var samples = SampleData.LoadDecks();
            if (index < 0 || index >= samples.Count)
                return;
            var copy = Clone(samples[index]);
            copy.Id = NewId();
            copy.Name = TrimName((copy.Name ?? "") + " 복사본");
            Open(copy);
            _savedJson = null;
            CloseLibrary();
            Refresh();
            SetStatus("샘플 덱을 복사했습니다. 저장해야 보관됩니다.");
        }

        static string TrimName(string name) => name.Length > MaxNameLength ? name.Substring(0, MaxNameLength) : name;

        // ------------------------------------------------------------------ 이름표

        static T Step<T>(T[] options, T current, int delta)
        {
            int index = Array.IndexOf(options, current);
            return options[((index < 0 ? 0 : index) + delta + options.Length) % options.Length];
        }

        static string SourceName(SourceFilter s) => s == SourceFilter.Mine ? "내 카드" : s == SourceFilter.Sample ? "샘플" : "전체";

        static string CostName(int? cost)
        {
            if (!cost.HasValue) return "전체";
            return cost.Value == DeckReport.CostCurveBuckets - 1 ? cost.Value + " 이상" : cost.Value.ToString();
        }

        static string SortName(SortMode mode)
        {
            switch (mode)
            {
                case SortMode.ScoreHigh: return "점수 높은";
                case SortMode.ScoreLow: return "점수 낮은";
                case SortMode.Name: return "이름";
                case SortMode.Recent: return "최근 만든";
                default: return "코스트";
            }
        }
    }
}
