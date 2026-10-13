using System;
using TCG.Client.Common;
using TCG.Core.Data;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TCG.Client.UserAssets
{
    /// <summary>
    /// 카드 그림 영역 지정 창 (GameDesign §9, SNS 프로필 사진식): 끌어서 위치, 휠·버튼으로 확대.
    /// 창 안에 보이는 그대로가 저장된다. 영역 계산은 Core의 <see cref="ArtCrop"/>을 쓴다.
    /// </summary>
    public sealed class ArtCropDialog : MonoBehaviour
    {
        const float ViewWidth = 720f;
        const int ZoomStep = 10;

        Texture2D _source;
        AssetLimits _limits;
        Action<CropRect?> _done;
        RawImage _view;
        TextMeshProUGUI _zoomLabel;
        Canvas _canvas;
        int _zoom = 100;
        float _cx, _cy;
        CropRect _crop;

        public CropRect Crop => _crop;

        /// <summary>창을 띄운다. 확인하면 영역, 취소하면 null로 done이 불리고 창은 닫힌다(원본 텍스처는 호출한 쪽이 정리).</summary>
        public static ArtCropDialog Open(Transform parent, Texture2D source, AssetLimits limits, Action<CropRect?> done)
        {
            UiFactory.EnsureEventSystem();
            var canvas = UiFactory.OverlayCanvas(parent, "ArtCropDialog", 500);
            var dialog = canvas.gameObject.AddComponent<ArtCropDialog>();
            dialog._canvas = canvas;
            dialog._source = source;
            dialog._limits = limits;
            dialog._done = done;
            dialog._cx = source.width * 0.5f;
            dialog._cy = source.height * 0.5f;
            dialog.Build();
            dialog.Apply();
            return dialog;
        }

        void Build()
        {
            var root = UiFactory.Stretch(transform, "Dim");
            root.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.75f);

            float viewHeight = ViewWidth * _limits.ArtHeight / _limits.ArtWidth;
            UiFactory.Text(root, "Title", "카드 그림 영역 정하기", new Vector2(0.5f, 0.5f), new Vector2(0, viewHeight * 0.5f + 90), new Vector2(900, 60), 40);
            UiFactory.Text(root, "Hint", "끌어서 위치를 옮기고, 마우스 휠이나 +/− 로 확대합니다", new Vector2(0.5f, 0.5f),
                new Vector2(0, viewHeight * 0.5f + 40), new Vector2(1000, 40), 24).color = new Color(1f, 1f, 1f, 0.7f);

            UiFactory.Panel(root, "Frame", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(ViewWidth + 12, viewHeight + 12), Color.white);
            var viewRect = UiFactory.Rect(root, "View", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(ViewWidth, viewHeight));
            _view = viewRect.gameObject.AddComponent<RawImage>();
            _view.texture = _source;
            var handler = viewRect.gameObject.AddComponent<CropViewHandler>();
            handler.Dragged = OnDrag;
            handler.Scrolled = dir => SetZoom(_zoom + dir * ZoomStep);

            float y = -viewHeight * 0.5f - 60;
            UiFactory.Button(root, "ZoomOut", "−", new Vector2(0.5f, 0.5f), new Vector2(-130, y), new Vector2(70, 60), new Color(0.3f, 0.3f, 0.35f),
                () => SetZoom(_zoom - ZoomStep), 40);
            _zoomLabel = UiFactory.Text(root, "Zoom", "", new Vector2(0.5f, 0.5f), new Vector2(0, y), new Vector2(160, 60), 30);
            UiFactory.Button(root, "ZoomIn", "+", new Vector2(0.5f, 0.5f), new Vector2(130, y), new Vector2(70, 60), new Color(0.3f, 0.3f, 0.35f),
                () => SetZoom(_zoom + ZoomStep), 40);

            UiFactory.Button(root, "Cancel", "취소", new Vector2(0.5f, 0.5f), new Vector2(-130, y - 90), new Vector2(220, 70), new Color(0.35f, 0.35f, 0.38f),
                () => Close(null));
            UiFactory.Button(root, "Confirm", "확인", new Vector2(0.5f, 0.5f), new Vector2(130, y - 90), new Vector2(220, 70), new Color(0.2f, 0.5f, 0.3f),
                () => Close(_crop));
        }

        void OnDrag(Vector2 screenDelta)
        {
            var size = _view.rectTransform.rect.size * _canvas.scaleFactor;
            if (size.x <= 0f || size.y <= 0f)
                return;
            _cx -= screenDelta.x * _crop.Width / size.x;
            _cy -= screenDelta.y * _crop.Height / size.y;
            Apply();
        }

        public void SetZoom(int zoom)
        {
            _zoom = Mathf.Clamp(zoom, 100, _limits.MaxArtZoomPercent);
            Apply();
        }

        /// <summary>테스트·외부 조작용: 원본 픽셀 기준 중심 지정.</summary>
        public void SetCenter(int x, int y)
        {
            _cx = x;
            _cy = y;
            Apply();
        }

        void Apply()
        {
            _crop = ArtCrop.Compute(_source.width, _source.height, _limits.ArtWidth, _limits.ArtHeight, _zoom,
                Mathf.RoundToInt(_cx), Mathf.RoundToInt(_cy), _limits.MaxArtZoomPercent);
            // 끝에 닿으면 중심도 그 자리에 멈춘다 (계속 끌어도 밖으로 쌓이지 않게).
            float halfW = _crop.Width * 0.5f, halfH = _crop.Height * 0.5f;
            _cx = Mathf.Clamp(_cx, halfW, _source.width - halfW);
            _cy = Mathf.Clamp(_cy, halfH, _source.height - halfH);

            _view.uvRect = new Rect((float)_crop.X / _source.width, (float)_crop.Y / _source.height,
                (float)_crop.Width / _source.width, (float)_crop.Height / _source.height);
            _zoomLabel.text = _zoom + "%";
        }

        void Close(CropRect? result)
        {
            var done = _done;
            _done = null;
            Destroy(gameObject);
            done?.Invoke(result);
        }

        sealed class CropViewHandler : MonoBehaviour, IDragHandler, IScrollHandler
        {
            public Action<Vector2> Dragged;
            public Action<int> Scrolled;

            public void OnDrag(PointerEventData eventData) => Dragged?.Invoke(eventData.delta);

            public void OnScroll(PointerEventData eventData)
            {
                float y = eventData.scrollDelta.y;
                if (y != 0f)
                    Scrolled?.Invoke(y > 0f ? 1 : -1);
            }
        }
    }
}
