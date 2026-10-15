using TCG.Client.Common;
using TCG.Core.State;
using TMPro;
using UnityEngine;

namespace TCG.Client.Interaction
{
    /// <summary>
    /// 공격 결과 미리보기 (GameDesign §11.2): 공격 화살표를 고를 수 있는 대상에 올리면 양쪽 예상 피해를 띄우고,
    /// 쓰러지면 "처치"를 붙인다. 공격력·체력만으로 계산한 예상값이다 (피해 트리거 효과는 넣지 않음).
    /// </summary>
    public sealed class AttackPreview : MonoBehaviour
    {
        static readonly Color DamageColor = new Color(1f, 0.35f, 0.3f);
        static readonly Color KillColor = new Color(1f, 0.85f, 0.3f);
        const float LabelHeight = 0.6f;

        TextMeshPro _onTarget, _onAttacker;

        public static AttackPreview Create(Transform parent)
        {
            var go = new GameObject("AttackPreview");
            go.transform.SetParent(parent, false);
            var preview = go.AddComponent<AttackPreview>();
            preview._onTarget = Label(go.transform, "Target");
            preview._onAttacker = Label(go.transform, "Attacker");
            go.SetActive(false);
            return preview;
        }

        static TextMeshPro Label(Transform parent, string name)
        {
            var label = Shapes.Bold(Shapes.Label(parent, name, "", Vector3.zero, 5f, DamageColor, 2.4f, 0.8f));
            label.outlineWidth = 0.25f;
            label.outlineColor = Color.black;
            return label;
        }

        /// <summary>attacker가 target(카드 또는 영웅 ID)을 공격했을 때의 예상을 보여 준다.</summary>
        public void Show(GameState state, int attackerId, int targetId, Vector3 attackerPos, Vector3 targetPos)
        {
            var attacker = state.FindEntity(attackerId);
            if (attacker == null)
            {
                Hide();
                return;
            }

            int attack = attacker.Attack ?? 0;
            var hero = state.FindHero(targetId);
            var minion = hero == null ? state.FindEntity(targetId) : null;
            int targetHealth = hero != null ? hero.Health : minion?.Health ?? 0;
            int counter = minion?.Attack ?? 0; // 영웅은 반격하지 않는다

            gameObject.SetActive(true);
            Set(_onTarget, targetPos, attack, targetHealth);
            if (minion != null)
                Set(_onAttacker, attackerPos, counter, attacker.Health ?? 0);
            else
                _onAttacker.gameObject.SetActive(false);
        }

        static void Set(TextMeshPro label, Vector3 at, int damage, int health)
        {
            label.gameObject.SetActive(damage > 0);
            if (damage <= 0)
                return;
            bool kills = damage >= health;
            label.text = kills ? $"-{damage} <size=70%>처치</size>" : $"-{damage}";
            label.color = kills ? KillColor : DamageColor;
            label.transform.position = at + Vector3.up * LabelHeight;
        }

        public void Hide() => gameObject.SetActive(false);
    }
}
