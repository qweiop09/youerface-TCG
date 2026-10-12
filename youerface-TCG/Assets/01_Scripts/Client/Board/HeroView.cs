using TCG.Client.Cards;
using TCG.Client.Common;
using TCG.Core.State;
using TMPro;
using UnityEngine;

namespace TCG.Client.Board
{
    /// <summary>영웅 표시: 원판 + 이름표 + 체력. 체력은 화면에 보이는 값을 따로 든다 (연출 중 단계별로 바뀜).</summary>
    public sealed class HeroView : MonoBehaviour
    {
        TextMeshPro _health;

        public int Health { get; private set; }
        public int MaxHealth { get; private set; }

        public static HeroView Create(Transform parent, string label, Vector3 position, Color color)
        {
            var go = new GameObject($"Hero {label}");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            var view = go.AddComponent<HeroView>();

            Shapes.Disc(go.transform, "Plate", Vector3.zero, 1.5f, 0.12f, color);
            Shapes.Label(go.transform, "Name", label, new Vector3(0, 0.08f, 0.1f), 3f, Color.white, 1.4f, 0.5f);
            var badge = new GameObject("HealthBadge").transform;
            badge.SetParent(go.transform, false);
            badge.localPosition = new Vector3(0.62f, 0, -0.5f);
            Shapes.Disc(badge, "Disc", new Vector3(0, 0.08f, 0), 0.55f, 0.05f, CardPalette.HealthBadge);
            view._health = Shapes.Bold(Shapes.Label(badge, "Value", "", new Vector3(0, 0.12f, 0), 3.4f, Color.white, 0.6f, 0.4f));
            return view;
        }

        /// <summary>실제 상태와 맞춘다. 표시가 달랐으면 true.</summary>
        public bool Refresh(HeroState hero)
        {
            bool changed = Health != hero.Health || MaxHealth != hero.MaxHealth;
            SetHealth(hero.Health, hero.MaxHealth);
            return changed;
        }

        public void SetHealth(int health, int maxHealth)
        {
            Health = health;
            MaxHealth = maxHealth;
            _health.text = health.ToString();
            _health.color = health < maxHealth ? CardPalette.Worse : CardPalette.NumberLight;
        }

        public void AddHealth(int delta) => SetHealth(Health + delta, MaxHealth);
    }
}
