using System.Collections.Generic;
using System.Text;
using TCG.Client.Common;
using TCG.Client.UserAssets;
using TCG.Core.Data;
using TCG.Core.State;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TCG.Client.Cards
{
    /// <summary>
    /// 크게 보는 카드 (uGUI): 이름·종류·코스트·능력치·사용 횟수·키워드·효과 설명(<see cref="EffectText"/>로 묶은 문장)·카드 설명(flavor).
    /// 카드 확인 창·상대 카드 크게 띄우기·카드 제작기 미리보기·덱 편집 미리보기가 모두 이것을 쓴다 (GameDesign §11.1).
    ///
    /// 카드 밖 왼쪽 위 (<see cref="SideNotes"/>가 켜져 있을 때):
    /// - 키워드 설명 창: 키워드 1개당 1줄("일회성: …"), 아래로 쌓인다. 창들은 폭·좌우 여백이 같다.
    /// - 소환물 미니 카드: 소환물을 만드는 효과마다. 클릭하면 이 카드 자리에서 그 소환물을 자세히 보고, "← 돌아가기"로 돌아온다.
    /// </summary>
    public sealed class CardDetailView : MonoBehaviour
    {
        public const float Width = 420f;
        public const float Height = 600f;

        Image _frame, _art;
        RawImage _artImage;
        TextMeshProUGUI _name, _type, _cost, _attack, _health, _usage, _body;
        GameObject _attackBadge, _healthBadge, _usageBadge, _costBadge;

        public const float SideWidth = 270f;
        const float SideGap = 14f;
        const float MiniScale = 0.55f;

        RectTransform _side;
        readonly List<RectTransform> _navTargets = new List<RectTransform>();
        readonly Stack<Entity> _history = new Stack<Entity>();
        Entity _shown;

        public RectTransform Rect => (RectTransform)transform;

        /// <summary>카드 밖 왼쪽 위의 키워드 설명 창·소환물 미니 카드를 보일지. 미니 카드 자체는 끈다.</summary>
        public bool SideNotes { get; set; } = true;

        public static CardDetailView Create(Transform parent)
        {
            var frame = UiFactory.Panel(parent, "CardDetail", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(Width, Height), Color.gray);
            var view = frame.gameObject.AddComponent<CardDetailView>();
            view.Build(frame);
            return view;
        }

        void Build(Image frame)
        {
            _frame = frame;
            var t = frame.transform;
            _art = UiFactory.Panel(t, "Art", new Vector2(0.5f, 1f), new Vector2(0, -150), new Vector2(Width - 40, 210), Color.white);
            _type = UiFactory.Text(_art.transform, "Type", "", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(Width - 40, 210), 64);
            _type.color = new Color(1, 1, 1, 0.55f);
            // 유저 그림: 그림 칸 높이에 맞춰 카드 그림 비율 그대로 가운데에.
            var limits = UserAssetService.Limits;
            _artImage = UiFactory.Rect(_art.transform, "ArtImage", new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(210f * limits.ArtWidth / limits.ArtHeight, 210)).gameObject.AddComponent<RawImage>();
            _artImage.raycastTarget = false;
            _artImage.gameObject.SetActive(false);

            UiFactory.Panel(t, "NameBanner", new Vector2(0.5f, 1f), new Vector2(0, -290), new Vector2(Width - 24, 56), CardPalette.Banner);
            _name = UiFactory.Text(t, "Name", "", new Vector2(0.5f, 1f), new Vector2(0, -290), new Vector2(Width - 40, 56), 34);

            UiFactory.Panel(t, "TextBox", new Vector2(0.5f, 0f), new Vector2(0, 150), new Vector2(Width - 40, 230), new Color(0.96f, 0.93f, 0.86f));
            _body = UiFactory.Text(t, "Body", "", new Vector2(0.5f, 0f), new Vector2(0, 150), new Vector2(Width - 64, 214), 26, TextAlignmentOptions.TopLeft);
            _body.color = new Color(0.08f, 0.07f, 0.06f);
            _body.fontWeight = FontWeight.Medium;
            _body.textWrappingMode = TextWrappingModes.Normal;
            _body.overflowMode = TextOverflowModes.Ellipsis;
            _body.margin = new Vector4(0, 10, 0, 0); // 글씨 칸 위쪽 여백 (8 → 18)

            _cost = UiFactory.Badge(t, "Cost", new Vector2(0f, 1f), new Vector2(40, -40), 74, CardPalette.CostBadge, 42);
            _costBadge = _cost.transform.parent.gameObject;
            _attack = UiFactory.Badge(t, "Attack", new Vector2(0f, 0f), new Vector2(40, 38), 74, CardPalette.AttackBadge, 42);
            _attackBadge = _attack.transform.parent.gameObject;
            _health = UiFactory.Badge(t, "Health", new Vector2(1f, 0f), new Vector2(-40, 38), 74, CardPalette.HealthBadge, 42);
            _healthBadge = _health.transform.parent.gameObject;
            _usage = UiFactory.Badge(t, "Usage", new Vector2(1f, 0f), new Vector2(-40, 38), 74, CardPalette.UsageBadge, 42);
            _usageBadge = _usage.transform.parent.gameObject;
        }

        /// <summary>현재 수치(코스트·공격력·체력·사용 횟수)와 함께 카드를 보여 준다. 소환물 보기 기록은 지운다.</summary>
        public void Show(Entity entity)
        {
            _history.Clear();
            Render(entity);
        }

        /// <summary>화면 좌표가 소환물 미니 카드·돌아가기 같은 이 카드 안의 버튼 위인가 (확인 창이 클릭으로 닫히지 않게).</summary>
        public bool IsOverNavigation(Vector2 screen)
        {
            foreach (var rect in _navTargets)
            {
                if (rect != null && rect.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(rect, screen, null))
                    return true;
            }
            return false;
        }

        void Render(Entity entity)
        {
            _shown = entity;
            var def = entity.Definition;
            _frame.color = CardPalette.Body(def.Type);
            _art.color = CardPalette.Art(def.Type);
            _type.text = KoreanTerms.Of(def.Type);
            var art = UserAssetService.Cache.Texture(def.Art?.Image);
            _artImage.texture = art;
            _artImage.gameObject.SetActive(art != null);
            _type.gameObject.SetActive(art == null);
            _name.text = def.Name ?? "";

            _costBadge.SetActive(def.Type != CardType.Summon);
            _cost.text = entity.Cost.ToString();

            bool stats = entity.Attack.HasValue && entity.Health.HasValue;
            _attackBadge.SetActive(stats);
            _healthBadge.SetActive(stats);
            if (stats)
            {
                _attack.text = entity.Attack.Value.ToString();
                _attack.color = CardPalette.NumberDark;
                _health.text = entity.Health.Value.ToString();
            }
            _usageBadge.SetActive(!stats && entity.Usage.HasValue);
            if (entity.Usage.HasValue)
                _usage.text = entity.Usage.Value.ToString();

            _body.text = BodyText(entity);
            BuildSide(entity);
        }

        /// <summary>본문: 키워드 이름(굵게, 가운데 정렬) → 효과 문장 → 카드 설명(기울임, 흐리게, flavor가 true일 때만 — 자세히 보기 전용).</summary>
        public static string BodyText(Entity entity, bool flavor = true)
        {
            var def = entity.Definition;
            var sb = new StringBuilder();
            if (entity.Keywords.Count > 0)
            {
                // 키워드 줄은 가운데 정렬 (좌우 여백이 같게). 효과 문장은 왼쪽 정렬 그대로.
                sb.Append("<align=center>");
                for (int i = 0; i < entity.Keywords.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append("<b>").Append(KoreanTerms.Of(entity.Keywords[i])).Append("</b>");
                }
                sb.Append("</align>\n");
            }
            foreach (var sentence in EffectText.Sentences(def, "<color=#2A6FA8>(선택)</color>"))
                sb.Append(sentence).Append('\n');
            if (flavor && !string.IsNullOrEmpty(def.Flavor))
                sb.Append("<size=80%><i><color=#6B6155>").Append(def.Flavor).Append("</color></i></size>\n");
            if (sb.Length == 0 && flavor)
                sb.Append("<i>효과 없음</i>");
            return sb.ToString().TrimEnd('\n');
        }

        // ------------------------------------------------------------------ 카드 밖 왼쪽 위

        void BuildSide(Entity entity)
        {
            _navTargets.Clear();
            if (_side != null)
                Destroy(_side.gameObject);
            _side = null;
            if (!SideNotes)
                return;

            _side = UiFactory.Rect(transform, "SideNotes", new Vector2(0f, 1f), Vector2.zero, new Vector2(SideWidth, 0));
            _side.pivot = new Vector2(1f, 1f);
            _side.anchoredPosition = new Vector2(-SideGap, 0f);
            var layout = _side.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 6;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            _side.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            if (_history.Count > 0)
            {
                var parent = _history.Peek();
                var back = UiFactory.Button(_side, "Back", "← " + (parent.Definition.Name ?? "카드") + "(으)로", new Vector2(0.5f, 0.5f),
                    Vector2.zero, new Vector2(SideWidth, 44), new Color(0.22f, 0.25f, 0.3f), GoBack, 22);
                back.gameObject.AddComponent<LayoutElement>().preferredHeight = 44;
                _navTargets.Add((RectTransform)back.transform);
            }

            foreach (var keyword in entity.Keywords)
                Note($"<b>{KoreanTerms.Of(keyword)}</b>: {KoreanTerms.Describe(keyword)}");

            var effects = entity.Definition.Effects;
            if (effects != null)
            {
                foreach (var effect in effects)
                {
                    var summon = effect?.Action?.Type == ActionType.CreateSummon ? effect.Action.Summon : null;
                    if (summon != null)
                        Mini(summon, entity.OwnerIndex);
                }
            }
        }

        /// <summary>키워드 설명 1줄 창. 폭과 좌우 여백은 모든 창이 같다.</summary>
        void Note(string text)
        {
            var box = UiFactory.Panel(_side, "Note", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(SideWidth, 40), new Color(0.1f, 0.11f, 0.14f, 0.92f));
            box.raycastTarget = false;
            var pad = box.gameObject.AddComponent<VerticalLayoutGroup>();
            pad.padding = new RectOffset(14, 14, 8, 8);
            pad.childControlWidth = true;
            pad.childControlHeight = true;
            pad.childForceExpandWidth = true;
            pad.childForceExpandHeight = false;
            var label = UiFactory.Text(box.transform, "Text", text, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(SideWidth - 28, 30), 20,
                TextAlignmentOptions.TopLeft);
            label.textWrappingMode = TextWrappingModes.Normal;
        }

        /// <summary>소환물 미니 카드 (클릭하면 그 소환물을 이 자리에서 자세히 본다).</summary>
        void Mini(CardDefinition summon, int ownerIndex)
        {
            float h = Height * MiniScale;
            var entry = UiFactory.Rect(_side, "Summon", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(SideWidth, h + 34));
            entry.gameObject.AddComponent<LayoutElement>().preferredHeight = h + 34;
            var title = UiFactory.Text(entry, "Title", "소환물 (클릭해서 보기)", new Vector2(0.5f, 1f), new Vector2(0, -14), new Vector2(SideWidth, 28), 18);
            title.color = new Color(1f, 1f, 1f, 0.7f);

            var holder = UiFactory.Rect(entry, "Holder", new Vector2(0.5f, 0f), new Vector2(0, h * 0.5f), new Vector2(Width, Height));
            holder.localScale = Vector3.one * MiniScale;
            var mini = Create(holder);
            mini.SideNotes = false;
            mini.Show(new Entity(0, ownerIndex, summon));
            var button = mini.gameObject.AddComponent<Button>();
            button.onClick.AddListener(() => Navigate(summon, ownerIndex));
            _navTargets.Add(mini.Rect);
        }

        void Navigate(CardDefinition summon, int ownerIndex)
        {
            if (_shown != null)
                _history.Push(_shown);
            Render(new Entity(0, ownerIndex, summon));
        }

        void GoBack()
        {
            if (_history.Count > 0)
                Render(_history.Pop());
        }

    }
}
