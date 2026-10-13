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
    /// 크게 보는 카드 (uGUI): 이름·종류·코스트·능력치·사용 횟수·키워드(설명 포함)·효과 설명.
    /// 상대 카드 크게 띄우기에 쓰고, 나중에 카드 자세히 보기·카드 제작기 미리보기에도 쓴다.
    /// </summary>
    public sealed class CardDetailView : MonoBehaviour
    {
        public const float Width = 420f;
        public const float Height = 600f;

        Image _frame, _art;
        RawImage _artImage;
        TextMeshProUGUI _name, _type, _cost, _attack, _health, _usage, _body;
        GameObject _attackBadge, _healthBadge, _usageBadge, _costBadge;

        public RectTransform Rect => (RectTransform)transform;

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

            _cost = UiFactory.Badge(t, "Cost", new Vector2(0f, 1f), new Vector2(40, -40), 74, CardPalette.CostBadge, 42);
            _costBadge = _cost.transform.parent.gameObject;
            _attack = UiFactory.Badge(t, "Attack", new Vector2(0f, 0f), new Vector2(40, 38), 74, CardPalette.AttackBadge, 42);
            _attackBadge = _attack.transform.parent.gameObject;
            _health = UiFactory.Badge(t, "Health", new Vector2(1f, 0f), new Vector2(-40, 38), 74, CardPalette.HealthBadge, 42);
            _healthBadge = _health.transform.parent.gameObject;
            _usage = UiFactory.Badge(t, "Usage", new Vector2(1f, 0f), new Vector2(-40, 38), 74, CardPalette.UsageBadge, 42);
            _usageBadge = _usage.transform.parent.gameObject;
        }

        /// <summary>현재 수치(코스트·공격력·체력·사용 횟수)와 함께 카드를 보여 준다.</summary>
        public void Show(Entity entity, EffectDescriber describe)
        {
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

            var sb = new StringBuilder();
            foreach (var keyword in entity.Keywords)
                sb.Append("<b>").Append(KoreanTerms.Of(keyword)).Append("</b> — ").Append(KoreanTerms.Describe(keyword)).Append('\n');
            if (def.Effects != null)
            {
                foreach (var effect in def.Effects)
                {
                    string kind = effect.Activation == EffectActivation.Optional ? " <color=#2A6FA8>(선택)</color>" : "";
                    sb.Append("• ").Append(describe(effect)).Append(kind).Append('\n');
                }
            }
            if (sb.Length == 0)
                sb.Append("<i>효과 없음</i>");
            _body.text = sb.ToString().TrimEnd('\n');
        }

        public delegate string EffectDescriber(EffectDefinition effect);
    }
}
