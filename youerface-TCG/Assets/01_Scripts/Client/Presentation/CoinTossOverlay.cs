using System.Collections;
using TCG.Client.Common;
using TCG.Core.Data;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace TCG.Client.Presentation
{
    /// <summary>
    /// 대전 시작 코인 토스 (GameDesign §11.3). 코인이 튀어 올라 돌다가 "선"(금색) 또는 "후"(은색) 면으로 내려앉고,
    /// 선공/후공과 후공 보정(첫 턴 최대 코스트·시작 손패, RuleConfig 값)을 안내한다. 결과는 Core가 이미 정했고 여기서는 보여 주기만 한다.
    /// 시간은 연출 배속을 따르고, 안내 중 클릭하면 바로 넘긴다.
    /// </summary>
    public sealed class CoinTossOverlay : MonoBehaviour
    {
        public const float FlipSeconds = 1.3f;
        public const float HoldSeconds = 1.4f;
        const int HalfTurns = 9; // 홀수: 시작 면의 반대로 내려앉음 → 시작 면을 결과의 반대로 둔다.
        const float CoinSize = 240f;
        const float Jump = 170f;

        static readonly Color GoldRim = new Color(0.62f, 0.45f, 0.12f);
        static readonly Color GoldFace = new Color(0.98f, 0.8f, 0.3f);
        static readonly Color SilverRim = new Color(0.45f, 0.48f, 0.55f);
        static readonly Color SilverFace = new Color(0.82f, 0.85f, 0.9f);

        CanvasGroup _group;
        RectTransform _coin;
        Image _rim, _face;
        TextMeshProUGUI _faceText, _title, _detail;

        public bool IsShowing => _group != null && _group.alpha > 0.01f;
        /// <summary>지금까지 보여 준 횟수 (테스트·확인용).</summary>
        public int ShownCount { get; private set; }
        /// <summary>마지막 결과 문구 (테스트·확인용).</summary>
        public string TitleText => _title.text;

        public static CoinTossOverlay Create(Transform canvas)
        {
            var root = UiFactory.Stretch(canvas, "CoinToss");
            var overlay = root.gameObject.AddComponent<CoinTossOverlay>();
            overlay._group = root.gameObject.AddComponent<CanvasGroup>();
            overlay._group.alpha = 0f;
            overlay._group.blocksRaycasts = false;
            overlay._group.interactable = false;

            var backdrop = UiFactory.Stretch(root, "Backdrop").gameObject.AddComponent<Image>();
            backdrop.color = new Color(0f, 0f, 0f, 0.55f);
            backdrop.raycastTarget = false;

            overlay._coin = UiFactory.Rect(root, "Coin", new Vector2(0.5f, 0.5f), new Vector2(0, 60), new Vector2(CoinSize, CoinSize));
            overlay._rim = overlay._coin.gameObject.AddComponent<Image>();
            overlay._rim.sprite = UiFactory.CircleSprite();
            overlay._rim.raycastTarget = false;
            overlay._face = UiFactory.Panel(overlay._coin, "Face", new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one * (CoinSize - 26f), Color.white);
            overlay._face.sprite = UiFactory.CircleSprite();
            overlay._face.raycastTarget = false;
            overlay._faceText = UiFactory.Text(overlay._coin, "Mark", "", new Vector2(0.5f, 0.5f), new Vector2(0, 4), Vector2.one * CoinSize, 120);
            overlay._faceText.fontStyle = FontStyles.Bold;

            overlay._title = UiFactory.Text(root, "Title", "", new Vector2(0.5f, 0.5f), new Vector2(0, -150), new Vector2(1200, 100), 64);
            overlay._title.fontStyle = FontStyles.Bold;
            overlay._detail = UiFactory.Text(root, "Detail", "", new Vector2(0.5f, 0.5f), new Vector2(0, -220), new Vector2(1400, 60), 30);
            overlay._detail.color = new Color(1f, 0.85f, 0.5f);
            return overlay;
        }

        /// <summary>코인을 던져 결과를 보여 준다. iGoFirst = 화면 주인(아래쪽)이 선공인가.</summary>
        public IEnumerator Play(bool iGoFirst, RuleConfig rules)
        {
            ShownCount++;
            transform.SetAsLastSibling(); // 나중에 만든 턴 종료 버튼 등보다 위에
            _title.text = "";
            _detail.text = "";
            _coin.anchoredPosition = new Vector2(0, 60);
            SetFace(!iGoFirst);

            for (float t = 0; t < 0.2f; t += PresentationTime.DeltaTime)
            {
                _group.alpha = Mathf.Clamp01(t / 0.2f);
                yield return null;
            }
            _group.alpha = 1f;

            for (float t = 0; t < FlipSeconds; t += PresentationTime.DeltaTime)
            {
                Pose(iGoFirst, Mathf.Clamp01(t / FlipSeconds));
                yield return null;
            }
            Pose(iGoFirst, 1f);
            ShowResult(iGoFirst, rules);
            yield return Tween.Punch(_coin, 0.12f, 0.2f);

            for (float t = 0; t < HoldSeconds; t += PresentationTime.DeltaTime)
            {
                if (Pointer.current != null && Pointer.current.press.wasPressedThisFrame && t > 0.15f)
                    break;
                yield return null;
            }

            for (float t = 0; t < 0.2f; t += PresentationTime.DeltaTime)
            {
                _group.alpha = 1f - Mathf.Clamp01(t / 0.2f);
                yield return null;
            }
            _group.alpha = 0f;
        }

        /// <summary>결과 화면을 멈춘 채로 띄운다 (화면 캡처 확인용).</summary>
        public void ShowStill(bool iGoFirst, RuleConfig rules)
        {
            transform.SetAsLastSibling();
            Pose(iGoFirst, 1f);
            ShowResult(iGoFirst, rules);
            _group.alpha = 1f;
        }

        /// <summary>k = 0..1 진행도. 반 바퀴마다 면이 바뀌고, 포물선으로 튀었다가 제자리에 내려앉는다.</summary>
        void Pose(bool iGoFirst, float k)
        {
            float eased = 1f - (1f - k) * (1f - k) * (1f - k);
            float angle = eased * HalfTurns * Mathf.PI;
            float cos = Mathf.Cos(angle);
            int halves = Mathf.FloorToInt((angle + Mathf.PI / 2f) / Mathf.PI);
            bool startFace = !iGoFirst;
            SetFace(halves % 2 == 0 ? startFace : !startFace);
            _coin.localScale = new Vector3(Mathf.Max(0.04f, Mathf.Abs(cos)), 1f, 1f);
            _coin.anchoredPosition = new Vector2(0, 60 + Jump * 4f * k * (1f - k));
        }

        void SetFace(bool first)
        {
            _rim.color = first ? GoldRim : SilverRim;
            _face.color = first ? GoldFace : SilverFace;
            _faceText.text = first ? "선" : "후";
            _faceText.color = first ? new Color(0.45f, 0.28f, 0.05f) : new Color(0.25f, 0.28f, 0.35f);
        }

        void ShowResult(bool iGoFirst, RuleConfig rules)
        {
            _title.text = iGoFirst ? "선공입니다" : "후공입니다";
            _title.color = iGoFirst ? GoldFace : SilverFace;
            int firstHand = rules.FirstPlayerStartingHand, secondHand = rules.SecondPlayerStartingHand;
            _detail.text = iGoFirst
                ? $"첫 턴 최대 코스트 {rules.StartingMaxCost} · 시작 손패 {firstHand}장  (상대는 후공 보정을 받습니다)"
                : $"후공 보정: 최대 코스트 {rules.SecondPlayerStartingMaxCost} · 시작 손패 {secondHand}장으로 시작";
        }
    }
}
