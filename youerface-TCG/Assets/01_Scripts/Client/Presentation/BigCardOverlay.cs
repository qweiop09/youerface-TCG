using System.Collections;
using TCG.Client.Cards;
using TCG.Client.Common;
using TCG.Core.State;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TCG.Client.Presentation
{
    /// <summary>
    /// 상대가 낸 카드를 화면 가운데에 크게 띄운다 (GameDesign §3.4, 포켓몬 카드 포켓·마스터 듀얼 방식).
    /// 커지며 나타남 → 잠시 보여 줌 (클릭하면 바로 넘김) → 작아지며 사라짐. 시간은 연출 배속을 따른다.
    /// </summary>
    public sealed class BigCardOverlay : MonoBehaviour
    {
        public const float HoldSeconds = 1.1f;

        CardDetailView _card;
        TextMeshProUGUI _caption;
        CanvasGroup _group;

        public bool IsShowing => _group != null && _group.alpha > 0.01f;
        /// <summary>지금까지 띄운 횟수 (테스트·확인용).</summary>
        public int ShownCount { get; private set; }

        public static BigCardOverlay Create(Transform canvas)
        {
            var root = UiFactory.Rect(canvas, "BigCard", new Vector2(0.5f, 0.5f), new Vector2(0, 30), new Vector2(CardDetailView.Width, CardDetailView.Height + 70));
            var overlay = root.gameObject.AddComponent<BigCardOverlay>();
            overlay._group = root.gameObject.AddComponent<CanvasGroup>();
            overlay._group.alpha = 0f;
            overlay._group.blocksRaycasts = false;
            overlay._group.interactable = false;
            overlay._caption = UiFactory.Text(root, "Caption", "", new Vector2(0.5f, 1f), new Vector2(0, -18), new Vector2(600, 50), 30);
            overlay._caption.color = new Color(1f, 0.75f, 0.5f);
            overlay._card = CardDetailView.Create(root);
            overlay._card.Rect.anchoredPosition = new Vector2(0, -32);
            return overlay;
        }

        public IEnumerator Show(Entity entity, string caption, float hold = HoldSeconds)
        {
            ShownCount++;
            _caption.text = caption;
            _card.Show(entity);
            var rect = (RectTransform)transform;

            for (float t = 0; t < 0.18f; t += PresentationTime.DeltaTime)
            {
                float k = Mathf.Clamp01(t / 0.18f);
                _group.alpha = k;
                rect.localScale = Vector3.one * Mathf.Lerp(0.6f, 1f, k);
                yield return null;
            }
            _group.alpha = 1f;
            rect.localScale = Vector3.one;

            for (float t = 0; t < hold; t += PresentationTime.DeltaTime)
            {
                // 클릭(탭)하면 바로 넘긴다.
                if (Pointer.current != null && Pointer.current.press.wasPressedThisFrame && t > 0.15f)
                    break;
                yield return null;
            }

            for (float t = 0; t < 0.15f; t += PresentationTime.DeltaTime)
            {
                float k = Mathf.Clamp01(t / 0.15f);
                _group.alpha = 1f - k;
                rect.localScale = Vector3.one * Mathf.Lerp(1f, 0.8f, k);
                yield return null;
            }
            _group.alpha = 0f;
        }
    }
}
