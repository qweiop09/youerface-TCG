using TCG.Client.Common;
using TCG.Client.Presentation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TCG.Client.Board
{
    /// <summary>
    /// 코스트 보석 (GameDesign §11.3, 하스스톤 마나 크리스털 방식). 자리마다 "남은/최대" 숫자 + 최대 코스트 상한만큼의 보석 줄.
    /// - 채움: 남은 코스트가 늘면 새로 찬 보석이 차례로 튀어나오며 빛난다 (턴 시작).
    /// - 소모: 줄면 쓴 보석이 하얗게 번쩍인 뒤 어두워진다 (카드 사용).
    /// - 미리보기: <see cref="SetPreview"/>로 받은 코스트만큼 쓸 보석이 깜빡인다. 모자라면 남은 보석이 빨갛게 깜빡인다.
    /// - 부족: <see cref="Shake"/>로 줄이 좌우로 흔들린다.
    /// 숫자는 Core 상태(CostChanged 이벤트·Refresh)를 그대로 보여 주기만 한다.
    /// </summary>
    public sealed class CostGemsView : MonoBehaviour
    {
        const float GemSize = 26f;
        const float GemStep = 46f;
        const float NumberWidth = 96f;
        const float FillSeconds = 0.32f;
        const float FillStagger = 0.06f;
        const float SpendSeconds = 0.4f;
        const float ShakeSeconds = 0.4f;

        static readonly Color Filled = new Color(0.32f, 0.62f, 1f);
        static readonly Color Spent = new Color(0.1f, 0.13f, 0.2f);
        static readonly Color Rim = new Color(0.36f, 0.46f, 0.62f);
        static readonly Color Flash = new Color(0.9f, 0.97f, 1f);
        static readonly Color PreviewBlink = new Color(0.75f, 0.95f, 1f);
        static readonly Color ShortBlink = new Color(1f, 0.35f, 0.3f);

        sealed class Gem
        {
            public RectTransform Root;
            public Image Face;
            public Image Shine;
            /// <summary>채움 연출 시작까지 남은 시간(음수면 진행 중). float.NaN이면 없음.</summary>
            public float FillAt = float.NaN;
            public float SpendAt = float.NaN;
        }

        sealed class Row
        {
            public RectTransform Root;
            public TextMeshProUGUI Number;
            public Gem[] Gems = new Gem[0];
            public int Available, Max;
            public float ShakeTime;
        }

        readonly Row[] _rows = new Row[2];
        int _previewCost = -1;

        /// <summary>미리보기 중인 코스트 (내 쪽, 없으면 -1). 테스트·확인용.</summary>
        public int PreviewCost => _previewCost;
        /// <summary>흔들린 횟수 (테스트·확인용).</summary>
        public int ShakeCount { get; private set; }
        public int Available(int side) => _rows[side].Available;
        public int Max(int side) => _rows[side].Max;
        public string NumberText(int side) => _rows[side].Number.text;

        /// <summary>side 0 = 나(오른쪽 아래), 1 = 상대(오른쪽 위).</summary>
        public static CostGemsView Create(Transform canvas)
        {
            var go = new GameObject("CostGems", typeof(RectTransform));
            go.transform.SetParent(canvas, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var view = go.AddComponent<CostGemsView>();
            view._rows[0] = CreateRow(rect, "Mine", new Vector2(1f, 0f), new Vector2(-28f, 28f));
            view._rows[1] = CreateRow(rect, "Theirs", new Vector2(1f, 1f), new Vector2(-28f, -28f));
            return view;
        }

        static Row CreateRow(Transform parent, string name, Vector2 corner, Vector2 offset)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = corner;
            rect.anchoredPosition = offset;
            rect.sizeDelta = new Vector2(NumberWidth + GemStep * 10f, 48f);
            var number = UiFactory.Text(rect, "Number", "", new Vector2(0f, 0.5f), new Vector2(NumberWidth * 0.5f, 0f),
                new Vector2(NumberWidth, 48f), 32f);
            number.fontStyle = FontStyles.Bold;
            number.color = new Color(0.8f, 0.9f, 1f);
            return new Row { Root = rect, Number = number };
        }

        /// <summary>보석 개수(최대 코스트 상한)를 맞춘다. 상한이 바뀌었을 때만 다시 만든다.</summary>
        void EnsureGems(Row row, int count)
        {
            if (row.Gems.Length == count)
                return;
            foreach (var gem in row.Gems)
                Shapes.DestroyImmediateOrDeferred(gem.Root.gameObject);
            row.Gems = new Gem[count];
            row.Root.sizeDelta = new Vector2(NumberWidth + GemStep * count, 48f);
            for (int i = 0; i < count; i++)
            {
                var rim = UiFactory.Panel(row.Root, "Gem" + i, new Vector2(0f, 0.5f),
                    new Vector2(NumberWidth + GemStep * (i + 0.5f), 0f), Vector2.one * (GemSize + 6f), Rim);
                rim.raycastTarget = false;
                rim.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
                var face = UiFactory.Panel(rim.transform, "Face", new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one * GemSize, Filled);
                face.raycastTarget = false;
                var shine = UiFactory.Panel(face.transform, "Shine", new Vector2(0.3f, 0.7f), Vector2.zero, Vector2.one * (GemSize * 0.3f),
                    new Color(1f, 1f, 1f, 0.55f));
                shine.raycastTarget = false;
                row.Gems[i] = new Gem { Root = rim.rectTransform, Face = face, Shine = shine };
            }
        }

        /// <summary>연출 없이 바로 맞춘다 (보드 다시 그리기). 값이 같으면 진행 중인 채움·소모 연출은 그대로 둔다.</summary>
        public void Set(int side, int available, int max, int limit)
        {
            var row = _rows[side];
            EnsureGems(row, Mathf.Max(1, limit));
            if (row.Available != available || row.Max != max)
            {
                foreach (var gem in row.Gems)
                    gem.FillAt = gem.SpendAt = float.NaN;
            }
            row.Available = available;
            row.Max = max;
            Render(row, side);
        }

        /// <summary>코스트가 바뀜: 새로 찬 보석은 채움, 쓴 보석은 소모 연출.</summary>
        public void Change(int side, int available, int max)
        {
            var row = _rows[side];
            int oldAvailable = row.Available;
            row.Available = available;
            row.Max = max;
            for (int i = 0; i < row.Gems.Length; i++)
            {
                var gem = row.Gems[i];
                if (i >= oldAvailable && i < available)
                {
                    gem.FillAt = (i - oldAvailable) * FillStagger;
                    gem.SpendAt = float.NaN;
                }
                else if (i >= available && i < oldAvailable)
                {
                    gem.SpendAt = 0f;
                    gem.FillAt = float.NaN;
                }
            }
            Render(row, side);
        }

        /// <summary>내 쪽 미리보기: 이 코스트를 쓰면 꺼질 보석을 깜빡인다. 없으면 -1.</summary>
        public void SetPreview(int cost) => _previewCost = cost;

        /// <summary>코스트가 모자랄 때 줄을 흔든다.</summary>
        public void Shake(int side)
        {
            _rows[side].ShakeTime = ShakeSeconds;
            ShakeCount++;
        }

        void Update()
        {
            float dt = PresentationTime.DeltaTime;
            for (int side = 0; side < _rows.Length; side++)
            {
                var row = _rows[side];
                foreach (var gem in row.Gems)
                {
                    if (!float.IsNaN(gem.FillAt))
                    {
                        gem.FillAt -= dt;
                        if (gem.FillAt < -FillSeconds)
                            gem.FillAt = float.NaN;
                    }
                    if (!float.IsNaN(gem.SpendAt))
                    {
                        gem.SpendAt -= dt;
                        if (gem.SpendAt < -SpendSeconds)
                            gem.SpendAt = float.NaN;
                    }
                }
                if (row.ShakeTime > 0f)
                    row.ShakeTime = Mathf.Max(0f, row.ShakeTime - Time.unscaledDeltaTime);
                Render(row, side);
            }
        }

        void Render(Row row, int side)
        {
            row.Number.text = $"{row.Available}/{row.Max}";
            int shown = Mathf.Max(row.Max, row.Available);
            int preview = side == 0 ? _previewCost : -1;
            bool shortage = preview > row.Available;
            float blink = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 10f);

            for (int i = 0; i < row.Gems.Length; i++)
            {
                var gem = row.Gems[i];
                bool visible = i < shown || !float.IsNaN(gem.SpendAt);
                gem.Root.gameObject.SetActive(visible);
                if (!visible)
                    continue;

                bool filled = i < row.Available;
                Color color = filled ? Filled : Spent;
                float scale = 1f;
                gem.Shine.enabled = filled;

                if (!float.IsNaN(gem.FillAt))
                {
                    if (gem.FillAt > 0f)
                    {
                        // 차례를 기다리는 보석은 아직 빈 모습.
                        color = Spent;
                        gem.Shine.enabled = false;
                    }
                    else
                    {
                        float k = Mathf.Clamp01(-gem.FillAt / FillSeconds);
                        scale = k < 0.5f ? Mathf.Lerp(0.3f, 1.25f, k * 2f) : Mathf.Lerp(1.25f, 1f, (k - 0.5f) * 2f);
                        color = Color.Lerp(Flash, Filled, k);
                    }
                }
                else if (!float.IsNaN(gem.SpendAt))
                {
                    float k = Mathf.Clamp01(-gem.SpendAt / SpendSeconds);
                    scale = 1f + 0.2f * Mathf.Sin(k * Mathf.PI);
                    color = k < 0.25f ? Color.Lerp(Filled, Flash, k * 4f) : Color.Lerp(Flash, Spent, (k - 0.25f) / 0.75f);
                }
                else if (preview > 0 && filled)
                {
                    // 쓸 보석 = 남은 보석의 오른쪽 끝부터 cost개. 모자라면 남은 보석 전부를 빨갛게.
                    bool used = shortage || i >= row.Available - preview;
                    if (used)
                    {
                        color = Color.Lerp(Filled, shortage ? ShortBlink : PreviewBlink, blink);
                        scale = 1f + 0.08f * blink;
                    }
                }

                gem.Face.color = color;
                gem.Root.localScale = Vector3.one * scale;
            }

            float shake = row.ShakeTime > 0f ? Mathf.Sin(row.ShakeTime * 70f) * 12f * (row.ShakeTime / ShakeSeconds) : 0f;
            var anchored = row.Root.anchoredPosition;
            row.Root.anchoredPosition = new Vector2(-28f + shake, anchored.y);
            row.Number.color = side == 0 && shortage ? Color.Lerp(new Color(0.8f, 0.9f, 1f), ShortBlink, blink) : new Color(0.8f, 0.9f, 1f);
        }
    }
}
