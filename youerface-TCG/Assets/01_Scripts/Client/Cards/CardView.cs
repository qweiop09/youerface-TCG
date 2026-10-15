using TCG.Client.Common;
using TCG.Client.UserAssets;
using TCG.Core.Data;
using TCG.Core.State;
using TMPro;
using UnityEngine;

namespace TCG.Client.Cards
{
    /// <summary>
    /// 카드 1장의 임시 그림 (코드로 만든 판 + 글자). 카드는 XZ 평면에 누워 있고 위쪽이 +Z.
    /// 표시하는 숫자는 규칙 상태와 별개로 들고 있다 — 연출 중에는 이벤트에 따라 한 단계씩 바뀌고,
    /// 연출이 끝나면 <see cref="SetNumbersFrom"/>으로 실제 상태와 맞춘다.
    /// </summary>
    public sealed class CardView : MonoBehaviour
    {
        public const float Width = 1f;
        public const float Height = 1.4f;

        // 앞면 배치 (카드 로컬 Z, + = 카드 위쪽)
        const float ArtZ = 0.34f;
        const float ArtHeight = 0.5f;
        const float NameZ = -0.01f;
        const float TextZ = -0.37f;
        const float TextHeight = 0.46f;
        const float TextMaxSize = 0.95f;
        const float TextMinSize = 0.35f;
        const float Thickness = 0.03f;
        const float Top = Thickness * 0.5f + 0.002f;

        TextMeshPro _cost, _attack, _health, _usage;
        GameObject _faceRoot, _backRoot, _attackBadge, _healthBadge, _usageBadge, _costBadge, _glow;
        Renderer _body;
        CardDefinition _definition;

        public int EntityId { get; private set; }
        public int OwnerIndex { get; private set; }
        public CardType Type => _definition.Type;
        public CardDefinition Definition => _definition;
        public bool FaceUp { get; private set; }
        /// <summary>플레이어가 집고 있는 중. 배치 연출이 이 카드를 옮기지 않는다.</summary>
        public bool Held { get; set; }

        // 화면에 보이는 숫자
        public int Cost { get; private set; }
        public int? Attack { get; private set; }
        public int? Health { get; private set; }
        public int? MaxHealth { get; private set; }
        public int? Usage { get; private set; }

        public static CardView Create(Transform parent, Entity entity, bool faceUp)
        {
            var go = new GameObject($"Card {entity.Id} {entity.Definition.Name}");
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<CardView>();
            view.Build(entity);
            view.SetFaceUp(faceUp);
            return view;
        }

        void Build(Entity entity)
        {
            EntityId = entity.Id;
            OwnerIndex = entity.OwnerIndex;
            _definition = entity.Definition;
            var type = entity.Type;

            // 클릭·끌기 판정용
            var collider = gameObject.AddComponent<BoxCollider>();
            collider.size = new Vector3(Width, 0.08f, Height);

            _glow = Shapes.Box(transform, "Glow", new Vector3(0, -0.004f, 0), new Vector3(Width + 0.16f, 0.02f, Height + 0.16f), Color.white, unlit: true);
            _glow.SetActive(false);
            _body = Shapes.Box(transform, "Body", Vector3.zero, new Vector3(Width, Thickness, Height), CardPalette.Body(type)).GetComponent<Renderer>();

            _backRoot = new GameObject("Back");
            _backRoot.transform.SetParent(transform, false);
            Shapes.Box(_backRoot.transform, "Trim", new Vector3(0, Top, 0), new Vector3(Width * 0.78f, 0.002f, Height * 0.82f), CardPalette.BackTrim);
            Shapes.Box(_backRoot.transform, "Inner", new Vector3(0, Top + 0.002f, 0), new Vector3(Width * 0.7f, 0.002f, Height * 0.74f), CardPalette.Back);

            _faceRoot = new GameObject("Face");
            _faceRoot.transform.SetParent(transform, false);
            var face = _faceRoot.transform;

            // 앞면 (위 → 아래): 그림 → 이름 띠 → 효과 글씨 칸 (손패에서도 효과를 읽게, GameDesign §11.1).
            // 그림 자리: 유저 그림이 있으면 그림(6:5), 없으면 종류 이름을 크게 적는다.
            Shapes.Box(face, "Art", new Vector3(0, Top, ArtZ), new Vector3(0.86f, 0.002f, ArtHeight), CardPalette.Art(type));
            var art = UserAssetService.Cache.Texture(_definition.Art?.Image);
            if (art != null)
                Shapes.Picture(face, "ArtImage", new Vector3(0, Top + 0.003f, ArtZ), ArtHeight * 1.2f, ArtHeight, art);
            else
                Shapes.Label(face, "TypeLabel", KoreanTerms.Of(type), new Vector3(0, Top + 0.004f, ArtZ), 2.2f,
                    new Color(1f, 1f, 1f, 0.55f), 0.8f, 0.4f);

            Shapes.Box(face, "Banner", new Vector3(0, Top, NameZ), new Vector3(0.94f, 0.002f, 0.18f), CardPalette.Banner);
            Shapes.Label(face, "Name", _definition.Name ?? "", new Vector3(0, Top + 0.004f, NameZ), 1.5f, CardPalette.Text, 0.9f, 0.18f, ellipsis: true);

            // 효과 글씨: 키워드는 굵게, 칸에 맞게 글씨 크기 자동 축소. 아래 모서리 숫자 배지와 겹치지 않게 폭을 좁힌다.
            Shapes.Box(face, "TextBox", new Vector3(0, Top, TextZ), new Vector3(0.86f, 0.002f, TextHeight), CardPalette.TextBox);
            var body = Shapes.Label(face, "Body", CardDetailView.BodyText(entity, flavor: false), new Vector3(0, Top + 0.004f, TextZ + 0.01f),
                TextMaxSize, CardPalette.TextDark, 0.66f, TextHeight - 0.04f);
            body.enableAutoSizing = true;
            body.fontSizeMin = TextMinSize;
            body.fontSizeMax = TextMaxSize;
            body.textWrappingMode = TextWrappingModes.Normal;
            body.overflowMode = TextOverflowModes.Ellipsis;
            body.lineSpacing = -12f;

            _cost = Badge(face, "Cost", new Vector3(-0.4f, 0, 0.6f), CardPalette.CostBadge, out _costBadge);
            _attack = Badge(face, "Attack", new Vector3(-0.4f, 0, -0.6f), CardPalette.AttackBadge, out _attackBadge);
            _health = Badge(face, "Health", new Vector3(0.4f, 0, -0.6f), CardPalette.HealthBadge, out _healthBadge);
            _usage = Badge(face, "Usage", new Vector3(0.4f, 0, -0.6f), CardPalette.UsageBadge, out _usageBadge);

            SetNumbersFrom(entity);
        }

        static TextMeshPro Badge(Transform parent, string name, Vector3 pos, Color color, out GameObject badge)
        {
            badge = new GameObject(name);
            badge.transform.SetParent(parent, false);
            badge.transform.localPosition = pos;
            Shapes.Disc(badge.transform, "Disc", new Vector3(0, Top + 0.01f, 0), 0.3f, 0.02f, color);
            return Shapes.Bold(Shapes.Label(badge.transform, "Value", "", new Vector3(0, Top + 0.025f, 0), 2.2f, Color.white, 0.4f, 0.3f));
        }

        /// <summary>
        /// 가려진 카드(온라인 상대 손패 등, <see cref="TCG.Core.Net.HiddenCard"/>)였는데 정체가 공개되면 앞면을 새 정의로 다시 만든다.
        /// 위치·앞뒷면 상태는 유지. 바꿨으면 true.
        /// </summary>
        public bool Reveal(Entity entity)
        {
            if (!ReferenceEquals(_definition, TCG.Core.Net.HiddenCard.Definition) || ReferenceEquals(entity.Definition, _definition))
                return false;
            bool faceUp = FaceUp;
            for (int i = transform.childCount - 1; i >= 0; i--)
                Shapes.DestroyImmediateOrDeferred(transform.GetChild(i).gameObject);
            var collider = GetComponent<BoxCollider>();
            if (collider != null)
                Shapes.DestroyImmediateOrDeferred(collider);
            Build(entity);
            SetFaceUp(faceUp);
            gameObject.name = $"Card {entity.Id} {entity.Definition.Name}";
            return true;
        }

        public void SetFaceUp(bool faceUp)
        {
            FaceUp = faceUp;
            _faceRoot.SetActive(faceUp);
            _backRoot.SetActive(!faceUp);
            _body.sharedMaterial = MaterialCache.Get(faceUp ? CardPalette.Body(_definition.Type) : CardPalette.Back);
        }

        /// <summary>실제 상태의 수치로 표시를 맞춘다. 표시가 달랐으면 true.</summary>
        public bool SetNumbersFrom(Entity entity)
        {
            bool changed = Cost != entity.Cost || Attack != entity.Attack || Health != entity.Health
                || MaxHealth != entity.MaxHealth || Usage != entity.Usage;
            SetNumbers(entity.Cost, entity.Attack, entity.Health, entity.MaxHealth, entity.Usage);
            return changed;
        }

        public void SetNumbers(int cost, int? attack, int? health, int? maxHealth, int? usage)
        {
            Cost = cost;
            Attack = attack;
            Health = health;
            MaxHealth = maxHealth;
            Usage = usage;
            RefreshNumbers();
        }

        public void SetStats(int? attack, int? health, int? maxHealth, int cost) => SetNumbers(cost, attack, health, maxHealth, Usage);
        public void SetUsage(int usage) => SetNumbers(Cost, Attack, Health, MaxHealth, usage);
        public void AddHealth(int delta)
        {
            if (Health.HasValue)
                SetNumbers(Cost, Attack, Health + delta, MaxHealth, Usage);
        }

        void RefreshNumbers()
        {
            _cost.text = Cost.ToString();
            _cost.color = Cost < _definition.Cost ? CardPalette.BetterLight : Cost > _definition.Cost ? CardPalette.Worse : CardPalette.NumberLight;
            // 소환물은 손패로 오지 않으므로 코스트를 표시하지 않는다.
            _costBadge.SetActive(_definition.Type != CardType.Summon);

            bool hasStats = Attack.HasValue && Health.HasValue && MaxHealth.HasValue;
            _attackBadge.SetActive(hasStats);
            _healthBadge.SetActive(hasStats);
            if (hasStats)
            {
                _attack.text = Attack.Value.ToString();
                _attack.color = Attack.Value > (_definition.Attack ?? 0) ? CardPalette.BetterDark : CardPalette.NumberDark;
                _health.text = Health.Value.ToString();
                _health.color = Health.Value < MaxHealth.Value ? CardPalette.Worse
                    : MaxHealth.Value > (_definition.Health ?? 0) ? CardPalette.BetterLight : CardPalette.NumberLight;
            }

            bool hasUsage = Usage.HasValue && !hasStats;
            _usageBadge.SetActive(hasUsage);
            if (hasUsage)
                _usage.text = Usage.Value.ToString();
        }

        /// <summary>테두리 빛. null이면 끈다.</summary>
        public void SetGlow(Color? color)
        {
            _glow.SetActive(color.HasValue);
            if (color.HasValue)
                _glow.GetComponent<Renderer>().sharedMaterial = MaterialCache.Get(color.Value, unlit: true);
        }
    }
}
