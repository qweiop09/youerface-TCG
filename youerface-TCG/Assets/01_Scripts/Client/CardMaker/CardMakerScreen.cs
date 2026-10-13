using System;
using System.Collections.Generic;
using System.Text;
using TCG.Client.Cards;
using TCG.Client.Common;
using TCG.Client.UserAssets;
using TCG.Core.Data;
using TCG.Core.Scoring;
using TCG.Core.Serialization;
using TCG.Core.State;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TCG.Client.CardMaker
{
    /// <summary>
    /// 카드 제작 화면 (CardMaker 장면, M4-3). 왼쪽: 기본 정보·그림·사운드, 가운데: 미리보기·점수·검사 결과, 오른쪽: 효과 블록.
    /// 소환물 생성 효과의 소환물은 같은 화면에서 들어가 편집한다(위쪽 경로로 돌아옴).
    /// 점수는 최상위 카드 기준으로 항상 다시 계산한다(편집 중엔 음수 그대로, GameDesign §6.1).
    /// </summary>
    public sealed class CardMakerScreen : MonoBehaviour
    {
        public const string SceneName = "CardMaker";

        // 편집기 입력 범위 (규칙값 아님. 규칙 검사는 CardValidator)
        const int MaxNameLength = 20;
        const int MaxStat = 20;
        const int MaxAmount = 10;
        const int MaxUsage = 10;
        const int MaxConditionValue = 30;

        static readonly Color Background = new Color(0.06f, 0.07f, 0.09f);
        static readonly Color ErrorColor = new Color(1f, 0.5f, 0.45f);
        static readonly Color OkColor = new Color(0.6f, 0.9f, 0.6f);
        static readonly Color MutedColor = new Color(0.75f, 0.77f, 0.82f);

        readonly RuleConfig _rules = RuleConfig.CreateDefault();
        readonly ScoreTable _scoreTable = ScoreTable.CreateDefault();
        /// <summary>편집 경로: [0] = 최상위 카드, 그 뒤 = 들어가 편집 중인 소환물.</summary>
        readonly List<CardDefinition> _path = new List<CardDefinition>();

        Canvas _canvas;
        FormColumn _left, _right, _libraryList;
        GameObject _libraryPanel;
        string _savedJson;
        string _pendingDelete;
        CardDetailView _preview;
        TextMeshProUGUI _breadcrumb, _score, _scoreLines, _problems, _status;
        AudioSource _audio;

        public CardDefinition Card => _path.Count > 0 ? _path[0] : null;
        public CardDefinition Editing => _path.Count > 0 ? _path[_path.Count - 1] : null;
        public int CurrentScore { get; private set; }
        public IReadOnlyList<string> Problems { get; private set; } = Array.Empty<string>();
        public string StatusText => _status != null ? _status.text : "";
        public LocalCardLibrary Library { get; set; }
        /// <summary>카드를 지울 때 그 카드를 쓰는 덱을 알려 주려고 읽는다.</summary>
        public TCG.Client.DeckBuilder.LocalDeckLibrary Decks { get; set; }
        public bool IsDirty => Card != null && CardSerializer.Serialize(Card, false) != _savedJson;
        public bool LibraryOpen => _libraryPanel != null && _libraryPanel.activeSelf;

        void Start()
        {
            UiFactory.EnsureEventSystem();
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            Library = Library ?? new LocalCardLibrary();
            Decks = Decks ?? new TCG.Client.DeckBuilder.LocalDeckLibrary();
            Build();
            NewCard();
        }

        void Build()
        {
            _canvas = UiFactory.OverlayCanvas(transform, "CardMaker UI");
            var root = _canvas.transform;
            UiFactory.Stretch(root, "Background").gameObject.AddComponent<Image>().color = Background;

            UiFactory.Button(root, "Back", "← 시작 화면", new Vector2(0f, 1f), new Vector2(140, -50), new Vector2(240, 70), new Color(0.3f, 0.3f, 0.35f),
                () => SceneManager.LoadScene("Title"), 28);
            var title = UiFactory.Text(root, "Title", "카드 제작", new Vector2(0.5f, 1f), new Vector2(0, -50), new Vector2(600, 70), 44);
            title.fontStyle = FontStyles.Bold;
            var barColor = new Color(0.3f, 0.3f, 0.35f);
            UiFactory.Button(root, "New", "새 카드", new Vector2(1f, 1f), new Vector2(-110, -50), new Vector2(180, 70), barColor, NewCard, 28);
            UiFactory.Button(root, "Duplicate", "복제", new Vector2(1f, 1f), new Vector2(-300, -50), new Vector2(180, 70), barColor, Duplicate, 28);
            UiFactory.Button(root, "Library", "내 카드", new Vector2(1f, 1f), new Vector2(-490, -50), new Vector2(180, 70), barColor, OpenLibrary, 28);
            UiFactory.Button(root, "Save", "저장", new Vector2(1f, 1f), new Vector2(-680, -50), new Vector2(180, 70), new Color(0.2f, 0.5f, 0.3f), Save, 28);
            _breadcrumb = UiFactory.Text(root, "Path", "", new Vector2(0.5f, 1f), new Vector2(0, -105), new Vector2(1200, 40), 26);
            _breadcrumb.color = MutedColor;

            _left = FormColumn.Create(root, "Basics", new Vector2(0f, 0.5f), new Vector2(330, -60), new Vector2(600, 900));
            _right = FormColumn.Create(root, "Effects", new Vector2(1f, 0.5f), new Vector2(-350, -60), new Vector2(640, 900));

            var previewRoot = UiFactory.Rect(root, "Preview", new Vector2(0.5f, 0.5f), new Vector2(-30, 120),
                new Vector2(CardDetailView.Width, CardDetailView.Height));
            _preview = CardDetailView.Create(previewRoot);

            _score = UiFactory.Text(root, "Score", "", new Vector2(0.5f, 0.5f), new Vector2(-30, -225), new Vector2(560, 60), 40);
            _score.fontStyle = FontStyles.Bold;
            _scoreLines = UiFactory.Text(root, "ScoreLines", "", new Vector2(0.5f, 0.5f), new Vector2(-30, -290), new Vector2(560, 70), 22);
            _scoreLines.color = MutedColor;
            _scoreLines.textWrappingMode = TextWrappingModes.Normal;
            _problems = UiFactory.Text(root, "Problems", "", new Vector2(0.5f, 0.5f), new Vector2(-30, -390), new Vector2(560, 130), 22,
                TextAlignmentOptions.Top);
            _problems.textWrappingMode = TextWrappingModes.Normal;
            _problems.overflowMode = TextOverflowModes.Ellipsis;
            _status = UiFactory.Text(root, "Status", "", new Vector2(0.5f, 0f), new Vector2(0, 30), new Vector2(1400, 40), 24);
            _status.color = new Color(1f, 0.85f, 0.5f);

            BuildLibraryPanel(root);
        }

        // ------------------------------------------------------------------ 편집 대상

        public void NewCard()
        {
            Open(CardEditing.NewCard(NewId()));
            _savedJson = null;
            RefreshPreview();
            SetStatus("");
        }

        /// <summary>카드를 편집기에 연다(복사본을 편집).</summary>
        public void Open(CardDefinition card)
        {
            _path.Clear();
            _path.Add(CardEditing.Clone(card));
            CardEditing.Normalize(_path[0], null);
            _savedJson = CardSerializer.Serialize(_path[0], false);
            Rebuild();
        }

        public void EnterSummon(EffectDefinition effect)
        {
            if (effect?.Action?.Summon == null)
                return;
            _path.Add(effect.Action.Summon);
            ResetScroll();
            Rebuild();
        }

        public void LeaveTo(int depth)
        {
            if (depth < 0 || depth >= _path.Count - 1)
                return;
            _path.RemoveRange(depth + 1, _path.Count - depth - 1);
            ResetScroll();
            Rebuild();
        }

        static string NewId() => "c_" + Guid.NewGuid().ToString("N").Substring(0, 12);

        void ResetScroll()
        {
            _left.Content.anchoredPosition = Vector2.zero;
            _right.Content.anchoredPosition = Vector2.zero;
        }

        void SetStatus(string text) => _status.text = text;

        /// <summary>편집 칸 전체를 다시 쌓고 미리보기·점수를 갱신한다.</summary>
        void Rebuild()
        {
            BuildBasics();
            BuildEffects();
            RefreshPreview();
        }

        void Changed()
        {
            Rebuild();
        }

        // ------------------------------------------------------------------ 왼쪽: 기본 정보

        void BuildBasics()
        {
            var card = Editing;
            bool isSummon = card.Type == CardType.Summon;
            var f = _left;
            f.Clear();

            if (_path.Count > 1)
                f.Buttons("", ("← " + (_path[_path.Count - 2].Name ?? "") + "(으)로 돌아가기", () => LeaveTo(_path.Count - 2)));

            f.Header(isSummon ? "소환물" : "기본 정보");
            f.Input("이름", card.Name, MaxNameLength, v =>
            {
                card.Name = v;
                RefreshPreview();
            });
            if (!isSummon)
                f.CycleEnum("종류", card.Type, CardEditing.DeckTypes, KoreanTerms.Of, t =>
                {
                    CardEditing.SetType(card, t);
                    Changed();
                });
            if (!isSummon)
                f.Stepper("코스트", card.Cost, 0, _rules.MaxCostLimit, v => { card.Cost = v; Changed(); });

            if (card.Type == CardType.Minion)
            {
                f.Stepper("공격력", card.Attack ?? 0, 0, MaxStat, v => { card.Attack = v; Changed(); });
                f.Stepper("체력", card.Health ?? 1, 1, MaxStat, v => { card.Health = v; Changed(); });
            }

            if (card.Type == CardType.Summon)
            {
                f.Stepper("사용 횟수", card.Usage ?? 1, 1, MaxUsage, v => { card.Usage = v; Changed(); });
                f.CycleEnum("중복 생성", card.OnDuplicate ?? SummonDuplicateMode.AddNew, Values<SummonDuplicateMode>(), KoreanTerms.Of,
                    v => { card.OnDuplicate = v; Changed(); });
            }
            else if (card.Type == CardType.Companion)
            {
                f.Chips("사용 횟수", new List<(string, bool, Action)>
                {
                    ("영구 지속", card.Usage == null, () => { card.Usage = null; Changed(); }),
                    ("횟수 정하기", card.Usage != null, () => { card.Usage = card.Usage ?? 2; Changed(); }),
                });
                if (card.Usage != null)
                    f.Stepper("", card.Usage.Value, 1, MaxUsage, v => { card.Usage = v; Changed(); });
            }

            var keywords = CardEditing.AllowedKeywords(card.Type);
            if (keywords.Count > 0)
            {
                var chips = new List<(string, bool, Action)>();
                foreach (var k in keywords)
                {
                    var keyword = k;
                    chips.Add((KoreanTerms.Of(keyword), card.Keywords.Contains(keyword), () => { CardEditing.ToggleKeyword(card, keyword); Changed(); }));
                }
                f.Chips("키워드", chips);
            }

            f.Header("그림");
            var limits = UserAssetService.Limits;
            f.Note($"아무 크기의 PNG·JPG. 보일 영역을 정하면 {limits.ArtWidth}x{limits.ArtHeight}로 저장합니다.", MutedColor, 22);
            if (string.IsNullOrEmpty(card.Art?.Image))
                f.Buttons("", ("그림 가져오기", () => ImportArt(card)));
            else
                f.Buttons("", ("다른 그림", () => ImportArt(card)), ("그림 지우기", () => { card.Art.Image = null; Changed(); }));

            f.Header("사운드");
            f.Note($"OGG·WAV, {limits.MaxSoundMilliseconds / 1000f:0.#}초 이하.", MutedColor, 22);
            SoundRow(f, "등장음", card.Sounds.Enter, h => card.Sounds.Enter = h);
            SoundRow(f, "퇴장음", card.Sounds.Exit, h => card.Sounds.Exit = h);
            f.Finish();
        }

        void SoundRow(FormColumn f, string label, string hash, Action<string> set)
        {
            if (string.IsNullOrEmpty(hash))
                f.Buttons(label, ("가져오기", () => ImportSound(set)));
            else
                f.Buttons(label, ("▶ 듣기", () => PlaySound(hash)), ("바꾸기", () => ImportSound(set)), ("지우기", () => { set(null); Changed(); }));
        }

        // ------------------------------------------------------------------ 오른쪽: 효과

        void BuildEffects()
        {
            var card = Editing;
            var f = _right;
            f.Clear();
            f.Header("효과");
            if (card.Effects.Count == 0)
                f.Note("효과가 없습니다. 아래 버튼으로 추가하세요.", MutedColor);

            var score = ScoreCalculator.CalculateDetailed(card, _scoreTable);
            for (int i = 0; i < card.Effects.Count; i++)
                BuildEffect(f, card, i, EffectScore(score, i));

            f.Buttons("", ("+ 효과 추가", () => { card.Effects.Add(CardEditing.NewEffect()); Changed(); }));
            f.Finish();
        }

        static int EffectScore(ScoreResult score, int index)
        {
            foreach (var line in score.Lines)
            {
                if (line.Kind == ScoreLineKind.Effect && line.Index == index)
                    return line.Value;
            }
            return 0;
        }

        void BuildEffect(FormColumn f, CardDefinition card, int index, int score)
        {
            var effect = card.Effects[index];
            f.Space(6f);
            f.Buttons($"효과 {index + 1}  ({Signed(score)}점)",
                ("▲", () => { CardEditing.Move(card.Effects, index, -1); Changed(); }),
                ("▼", () => { CardEditing.Move(card.Effects, index, 1); Changed(); }),
                ("삭제", () => { card.Effects.RemoveAt(index); Changed(); }));
            f.Note(EffectText.Describe(effect), new Color(0.95f, 0.92f, 0.8f), 22);

            f.CycleEnum("트리거", effect.Trigger.Type, Values<TriggerType>(), EffectText.Trigger, v => { effect.Trigger.Type = v; Changed(); });
            f.CycleEnum("발동", effect.Activation, Values<EffectActivation>(), KoreanTerms.Of, v => { effect.Activation = v; Changed(); });

            for (int c = 0; c < effect.Conditions.Count; c++)
            {
                var condition = effect.Conditions[c];
                int ci = c;
                f.CycleEnum($"조건 {c + 1}", condition.Type, Values<ConditionType>(), KoreanTerms.Of,
                    v => { CardEditing.SetCondition(condition, v); Changed(); });
                if (condition.Type == ConditionType.CardTypeExists)
                    f.CycleEnum("  종류", condition.CardType ?? CardType.Field, Values<CardType>(), KoreanTerms.Of,
                        v => { condition.CardType = v; Changed(); });
                f.Stepper("  n", condition.Value, condition.Type == ConditionType.HandCountAtMost ? 0 : 1, MaxConditionValue,
                    v => { condition.Value = v; Changed(); });
                f.Buttons("", ($"조건 {c + 1} 삭제", () => { effect.Conditions.RemoveAt(ci); Changed(); }));
            }
            f.Buttons("", ("+ 조건 추가", () => { effect.Conditions.Add(CardEditing.NewCondition()); Changed(); }));

            f.CycleEnum("행동", effect.Action.Type, Values<ActionType>(), KoreanTerms.Of, v => { CardEditing.SetAction(effect, v); Changed(); });
            if (effect.Action.Type == ActionType.CreateSummon)
            {
                f.Buttons("소환물", ($"'{effect.Action.Summon?.Name}' 편집 →", () => EnterSummon(effect)));
            }
            else
            {
                f.CycleEnum("대상", effect.Target.Type, Values<TargetType>(), KoreanTerms.Of, v => { CardEditing.SetTarget(effect, v); Changed(); });
                if (effect.Target.Type == TargetType.RandomEnemyMinion)
                    f.Stepper("  개수", effect.Target.Count ?? 1, 1, _rules.MinionSlots, v => { effect.Target.Count = v; Changed(); });
                if (CardEditing.UsesAmount(effect.Action.Type))
                {
                    bool signed = effect.Action.Type == ActionType.ModifyAttack || effect.Action.Type == ActionType.ModifyHealth
                        || effect.Action.Type == ActionType.ModifyCost || effect.Action.Type == ActionType.ModifyUsage;
                    f.Stepper("수치", effect.Action.Amount ?? 1, signed ? -MaxAmount : 1, MaxAmount, v => { effect.Action.Amount = v; Changed(); });
                }
            }
            f.Space(10f);
        }

        // ------------------------------------------------------------------ 가운데: 미리보기·점수

        void RefreshPreview()
        {
            var editing = Editing;
            _preview.Show(new Entity(0, 0, editing), EffectText.Describe);

            var sb = new StringBuilder();
            for (int i = 0; i < _path.Count; i++)
            {
                if (i > 0) sb.Append("  ›  ");
                sb.Append(string.IsNullOrEmpty(_path[i].Name) ? "(이름 없음)" : _path[i].Name);
            }
            if (IsDirty)
                sb.Append("   — 저장 안 됨");
            _breadcrumb.text = sb.ToString();

            var result = ScoreCalculator.CalculateDetailed(Card, _scoreTable);
            CurrentScore = result.Total;
            _score.text = result.Total == result.Display ? $"점수 {result.Total}" : $"점수 {result.Total}  (카드 표시 {result.Display})";
            _score.color = result.Total < 0 ? new Color(0.6f, 0.75f, 1f) : Color.white;
            _scoreLines.text = DescribeLines(result);

            Problems = CardValidator.Validate(Card);
            if (Problems.Count == 0)
            {
                _problems.text = "규칙 검사 통과";
                _problems.color = OkColor;
            }
            else
            {
                _problems.text = "· " + string.Join("\n· ", Problems);
                _problems.color = ErrorColor;
            }
        }

        static string DescribeLines(ScoreResult result)
        {
            var sb = new StringBuilder();
            int effects = 0, keywords = 0;
            foreach (var line in result.Lines)
            {
                if (line.Kind == ScoreLineKind.Effect) { effects += line.Value; continue; }
                if (line.Kind == ScoreLineKind.Keyword) { keywords += line.Value; continue; }
                Append(sb, KoreanTerms.Of(line.Kind), line.Value);
            }
            if (keywords != 0) Append(sb, "키워드", keywords);
            if (effects != 0) Append(sb, "효과", effects);
            return sb.ToString();
        }

        static void Append(StringBuilder sb, string name, int value)
        {
            if (sb.Length > 0) sb.Append("  ·  ");
            sb.Append(name).Append(' ').Append(Signed(value));
        }

        static string Signed(int v) => v > 0 ? "+" + v : v.ToString();

        static IReadOnlyList<T> Values<T>() where T : Enum => (T[])Enum.GetValues(typeof(T));

        // ------------------------------------------------------------------ 저장·불러오기 (M4-4)

        /// <summary>규칙 검사에 걸려도 저장은 된다(만드는 중인 카드). 덱에 넣을 때 다시 검사한다.</summary>
        public void Save()
        {
            Library.Save(Card);
            _savedJson = CardSerializer.Serialize(Card, false);
            SetStatus(Problems.Count == 0 ? "저장했습니다." : "저장했습니다. 규칙 검사에 걸린 곳이 있어 덱에는 아직 넣을 수 없습니다.");
            RefreshPreview();
            if (LibraryOpen)
                RefreshLibrary();
        }

        /// <summary>지금 카드를 새 ID로 복사해 연다(저장은 따로).</summary>
        public void Duplicate()
        {
            var copy = CardEditing.Clone(Card);
            copy.Id = NewId();
            copy.Name = TrimName((copy.Name ?? "") + " 복사본");
            Open(copy);
            _savedJson = null;
            RefreshPreview();
            SetStatus("복사본을 만들었습니다. 저장해야 보관됩니다.");
        }

        static string TrimName(string name) => name.Length > MaxNameLength ? name.Substring(0, MaxNameLength) : name;

        void BuildLibraryPanel(Transform root)
        {
            var panel = UiFactory.Stretch(root, "LibraryPanel");
            panel.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.8f);
            UiFactory.Text(panel, "Title", "내 카드", new Vector2(0.5f, 1f), new Vector2(0, -90), new Vector2(800, 70), 44).fontStyle = FontStyles.Bold;
            UiFactory.Button(panel, "Close", "닫기", new Vector2(0.5f, 0f), new Vector2(0, 80), new Vector2(240, 70), new Color(0.3f, 0.3f, 0.35f),
                CloseLibrary, 28);
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
            var entries = Library.List();
            if (entries.Count == 0)
                f.Note("저장한 카드가 없습니다.", MutedColor, 28);
            foreach (var entry in entries)
            {
                var e = entry;
                int score = ScoreCalculator.Calculate(e.Card, _scoreTable);
                string label = $"{e.Card.Name}  ·  {KoreanTerms.Of(e.Card.Type)}  ·  코스트 {e.Card.Cost}  ·  점수 {score}";
                bool confirming = _pendingDelete == e.Id;
                f.Note(label, Color.white, 28);
                f.Buttons("",
                    ("열기", () => OpenFromLibrary(e.Id)),
                    (confirming ? "정말 삭제" : "삭제", () => DeleteFromLibrary(e.Id)));
                f.Space(10f);
            }
            f.Finish();
        }

        public void OpenFromLibrary(string id)
        {
            var card = Library.Load(id);
            if (card == null)
            {
                SetStatus("카드를 찾을 수 없습니다.");
                return;
            }
            Open(card);
            CloseLibrary();
            SetStatus($"'{card.Name}'을(를) 열었습니다.");
        }

        /// <summary>이 카드를 쓰는 내 덱 이름들.</summary>
        public List<string> DecksUsing(string id)
        {
            var names = new List<string>();
            foreach (var entry in Decks.List())
            {
                if (TCG.Core.Data.DeckEditing.Uses(entry.Deck, id))
                    names.Add(entry.Deck.Name);
            }
            return names;
        }

        /// <summary>두 번 눌러야 지운다 (첫 번째는 확인 표시, 쓰는 덱이 있으면 상태 줄에 안내).</summary>
        public void DeleteFromLibrary(string id)
        {
            if (_pendingDelete != id)
            {
                _pendingDelete = id;
                RefreshLibrary();
                var users = DecksUsing(id);
                SetStatus(users.Count == 0 ? "" :
                    $"이 카드를 쓰는 덱 {users.Count}개: {string.Join(", ", users)} — 지우면 덱에 '없는 카드'로 남습니다(덱 편집에서 뺄 수 있음).");
                return;
            }
            Library.Delete(id);
            _pendingDelete = null;
            if (Card != null && Card.Id == id)
                _savedJson = null;
            RefreshLibrary();
            RefreshPreview();
            SetStatus("카드를 지웠습니다.");
        }

        // ------------------------------------------------------------------ 그림·사운드 가져오기

        void ImportArt(CardDefinition card)
        {
            var limits = UserAssetService.Limits;
            string path = UserAssetService.Picker.PickFile("카드 그림 고르기", "이미지", limits.ImageExtensions);
            if (path == null)
                return;
            var source = UserAssetService.Importer.LoadSourceImage(path, out string error);
            if (source == null)
            {
                SetStatus(error);
                return;
            }
            ArtCropDialog.Open(transform, source, limits, crop =>
            {
                if (crop.HasValue)
                {
                    card.Art.Image = UserAssetService.Importer.SaveArt(source, crop.Value);
                    SetStatus("그림을 저장했습니다.");
                }
                Destroy(source);
                Changed();
            });
        }

        void ImportSound(Action<string> set)
        {
            var limits = UserAssetService.Limits;
            string path = UserAssetService.Picker.PickFile("사운드 고르기", "사운드", limits.SoundExtensions);
            if (path == null)
                return;
            StartCoroutine(UserAssetService.Importer.ImportSound(path, (hash, error) =>
            {
                if (hash == null)
                {
                    SetStatus(error);
                    return;
                }
                set(hash);
                SetStatus("사운드를 저장했습니다.");
                Changed();
            }));
        }

        void PlaySound(string hash)
        {
            StartCoroutine(PlayWhenLoaded(hash));
        }

        System.Collections.IEnumerator PlayWhenLoaded(string hash)
        {
            yield return UserAssetService.Cache.LoadClip(hash);
            var clip = UserAssetService.Cache.Clip(hash);
            if (clip != null)
                _audio.PlayOneShot(clip);
            else
                SetStatus("사운드를 찾을 수 없습니다.");
        }
    }
}
