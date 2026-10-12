using System.Text;
using TCG.Client.Common;
using TCG.Core.State;
using TMPro;
using UnityEngine;

namespace TCG.Client.Cards
{
    /// <summary>
    /// 카드 1장의 임시 그림 (코드로 만든 판 + 글자). 카드는 XZ 평면에 누워 있고 위쪽이 +Z.
    /// 크기: 가로 <see cref="Width"/>, 세로 <see cref="Height"/>.
    /// </summary>
    public sealed class CardView : MonoBehaviour
    {
        public const float Width = 1f;
        public const float Height = 1.4f;
        const float Thickness = 0.03f;
        const float Top = Thickness * 0.5f + 0.002f;

        TextMeshPro _cost, _attack, _health, _usage;
        GameObject _faceRoot, _backRoot, _attackBadge, _healthBadge, _usageBadge;
        bool _built;

        public int EntityId { get; private set; }
        public bool FaceUp { get; private set; }

        public static CardView Create(Transform parent, Entity entity, bool faceUp)
        {
            var go = new GameObject($"Card {entity.Id}");
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<CardView>();
            view.Build(entity, faceUp);
            return view;
        }

        void Build(Entity entity, bool faceUp)
        {
            EntityId = entity.Id;
            FaceUp = faceUp;
            var type = entity.Type;

            Shapes.Box(transform, "Body", Vector3.zero, new Vector3(Width, Thickness, Height),
                faceUp ? CardPalette.Body(type) : CardPalette.Back);

            _backRoot = new GameObject("Back");
            _backRoot.transform.SetParent(transform, false);
            Shapes.Box(_backRoot.transform, "Trim", new Vector3(0, Top, 0), new Vector3(Width * 0.78f, 0.002f, Height * 0.82f), CardPalette.BackTrim);
            Shapes.Box(_backRoot.transform, "Inner", new Vector3(0, Top + 0.002f, 0), new Vector3(Width * 0.7f, 0.002f, Height * 0.74f), CardPalette.Back);

            _faceRoot = new GameObject("Face");
            _faceRoot.transform.SetParent(transform, false);
            var face = _faceRoot.transform;
            var def = entity.Definition;

            // 그림 자리: 아직 카드 이미지가 없으므로 종류 이름을 크게 적는다.
            Shapes.Box(face, "Art", new Vector3(0, Top, 0.22f), new Vector3(0.86f, 0.002f, 0.72f), CardPalette.Art(type));
            Shapes.Label(face, "TypeLabel", KoreanTerms.Of(type), new Vector3(0, Top + 0.004f, 0.22f), 2.4f,
                new Color(1f, 1f, 1f, 0.55f), 0.8f, 0.4f);

            Shapes.Box(face, "Banner", new Vector3(0, Top, -0.24f), new Vector3(0.94f, 0.002f, 0.2f), CardPalette.Banner);
            Shapes.Label(face, "Name", def.Name ?? "", new Vector3(0, Top + 0.004f, -0.24f), 1.6f, CardPalette.Text, 0.9f, 0.2f, ellipsis: true);
            Shapes.Label(face, "Keywords", KeywordLine(entity), new Vector3(0, Top + 0.004f, -0.43f), 1.3f, CardPalette.Text, 0.9f, 0.18f, ellipsis: true);

            _cost = Badge(face, "Cost", new Vector3(-0.4f, 0, 0.6f), CardPalette.CostBadge, out _);
            _attack = Badge(face, "Attack", new Vector3(-0.4f, 0, -0.6f), CardPalette.AttackBadge, out _attackBadge);
            _health = Badge(face, "Health", new Vector3(0.4f, 0, -0.6f), CardPalette.HealthBadge, out _healthBadge);
            _usage = Badge(face, "Usage", new Vector3(0.4f, 0, -0.6f), CardPalette.UsageBadge, out _usageBadge);

            _built = true;
            Refresh(entity);
        }

        static TextMeshPro Badge(Transform parent, string name, Vector3 pos, Color color, out GameObject badge)
        {
            badge = new GameObject(name);
            badge.transform.SetParent(parent, false);
            badge.transform.localPosition = pos;
            Shapes.Disc(badge.transform, "Disc", new Vector3(0, Top + 0.01f, 0), 0.3f, 0.02f, color);
            return Shapes.Bold(Shapes.Label(badge.transform, "Value", "", new Vector3(0, Top + 0.025f, 0), 2.2f, Color.white, 0.4f, 0.3f));
        }

        static string KeywordLine(Entity entity)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < entity.Keywords.Count; i++)
            {
                if (i > 0) sb.Append(" · ");
                sb.Append(KoreanTerms.Of(entity.Keywords[i]));
            }
            return sb.ToString();
        }

        /// <summary>현재 수치로 숫자 표시를 갱신한다.</summary>
        public void Refresh(Entity entity)
        {
            if (!_built)
                return;

            _faceRoot.SetActive(FaceUp);
            _backRoot.SetActive(!FaceUp);
            if (!FaceUp)
                return;

            var def = entity.Definition;
            _cost.text = entity.Cost.ToString();
            _cost.color = entity.Cost < def.Cost ? CardPalette.BetterLight : entity.Cost > def.Cost ? CardPalette.Worse : CardPalette.NumberLight;
            // 소환물은 손패로 오지 않으므로 코스트를 표시하지 않는다.
            _cost.transform.parent.gameObject.SetActive(entity.Type != Core.Data.CardType.Summon);

            bool hasStats = entity.Attack.HasValue;
            _attackBadge.SetActive(hasStats);
            _healthBadge.SetActive(hasStats);
            if (hasStats)
            {
                _attack.text = entity.Attack.Value.ToString();
                _attack.color = entity.Attack.Value > (def.Attack ?? 0) ? CardPalette.BetterDark : CardPalette.NumberDark;
                _health.text = entity.Health.Value.ToString();
                _health.color = entity.Health.Value < entity.MaxHealth.Value ? CardPalette.Worse
                    : entity.MaxHealth.Value > (def.Health ?? 0) ? CardPalette.BetterLight : CardPalette.NumberLight;
            }

            bool hasUsage = entity.Usage.HasValue && !hasStats;
            _usageBadge.SetActive(hasUsage);
            if (hasUsage)
                _usage.text = entity.Usage.Value.ToString();
        }
    }
}
