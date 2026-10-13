using System.Collections;
using TCG.Client.Cards;
using TCG.Client.Common;
using TCG.Core.State;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace TCG.Client.Presentation
{
    /// <summary>
    /// 카드 확인: 보드·손패 카드를 짧게 클릭하면 화면 가운데에 상세 카드를 크게 띄우고 뒤를 어둡게 한다.
    /// 아무 데나 클릭하거나 Esc로 닫는다. 보는 동안에도 대전(컴퓨터 차례·연출)은 계속 진행된다.
    /// 어두운 배경이 UI 클릭을 막아, 닫는 클릭이 아래 버튼·보드로 새지 않는다.
    /// </summary>
    public sealed class CardInspectOverlay : MonoBehaviour
    {
        const float OpenSeconds = 0.18f;
        const float CloseSeconds = 0.12f;

        CanvasGroup _group;
        RectTransform _cardRoot;
        CardDetailView _card;
        int _openedFrame = -1;
        Coroutine _anim;

        public bool IsOpen { get; private set; }
        public int ShownEntityId { get; private set; } = -1;
        /// <summary>닫은 프레임 (같은 클릭이 보드 입력으로 이어지지 않게 BattleInput이 확인).</summary>
        public int ClosedFrame { get; private set; } = -1;

        public static CardInspectOverlay Create(Transform canvas)
        {
            var root = UiFactory.Stretch(canvas, "CardInspect");
            var overlay = root.gameObject.AddComponent<CardInspectOverlay>();
            overlay._group = root.gameObject.AddComponent<CanvasGroup>();
            overlay._group.alpha = 0f;
            overlay._group.blocksRaycasts = false;
            overlay._group.interactable = false;
            root.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);

            overlay._cardRoot = UiFactory.Rect(root, "Card", new Vector2(0.5f, 0.5f), new Vector2(0, 20),
                new Vector2(CardDetailView.Width, CardDetailView.Height));
            overlay._card = CardDetailView.Create(overlay._cardRoot);
            UiFactory.Text(root, "CloseHint", "아무 곳이나 클릭하면 닫힙니다", new Vector2(0.5f, 0f), new Vector2(0, 90), new Vector2(800, 50), 26)
                .color = new Color(1f, 1f, 1f, 0.6f);
            return overlay;
        }

        public void Show(Entity entity)
        {
            ShownEntityId = entity.Id;
            _card.Show(entity, EffectText.Describe);
            IsOpen = true;
            transform.SetAsLastSibling(); // 나중에 생긴 HUD 요소(체인 창 등)보다 위
            _openedFrame = Time.frameCount;
            _group.blocksRaycasts = true;
            Animate(Open());
        }

        public void Close()
        {
            if (!IsOpen)
                return;
            IsOpen = false;
            ClosedFrame = Time.frameCount;
            ShownEntityId = -1;
            _group.blocksRaycasts = false;
            Animate(Fade());
        }

        void Update()
        {
            if (!IsOpen || Time.frameCount == _openedFrame)
                return;
            bool click = Pointer.current != null && Pointer.current.press.wasPressedThisFrame;
            bool esc = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
            if (click || esc)
                Close();
        }

        void Animate(IEnumerator routine)
        {
            if (_anim != null)
                StopCoroutine(_anim);
            _anim = StartCoroutine(routine);
        }

        // 카드 확인은 사람이 직접 보는 것이라 연출 배속과 무관하게 실제 시간으로 움직인다.
        IEnumerator Open()
        {
            for (float t = 0; t < OpenSeconds; t += Time.unscaledDeltaTime)
            {
                float k = Mathf.Clamp01(t / OpenSeconds);
                _group.alpha = k;
                _cardRoot.localScale = Vector3.one * Mathf.Lerp(0.6f, 1f, k);
                yield return null;
            }
            _group.alpha = 1f;
            _cardRoot.localScale = Vector3.one;
        }

        IEnumerator Fade()
        {
            float from = _group.alpha;
            for (float t = 0; t < CloseSeconds; t += Time.unscaledDeltaTime)
            {
                float k = Mathf.Clamp01(t / CloseSeconds);
                _group.alpha = from * (1f - k);
                _cardRoot.localScale = Vector3.one * Mathf.Lerp(1f, 0.85f, k);
                yield return null;
            }
            _group.alpha = 0f;
        }
    }
}
