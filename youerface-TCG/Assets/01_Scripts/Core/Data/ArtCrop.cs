namespace TCG.Core.Data
{
    /// <summary>원본 이미지에서 잘라 낼 영역(픽셀).</summary>
    public readonly struct CropRect
    {
        public readonly int X, Y, Width, Height;

        public CropRect(int x, int y, int width, int height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }
    }

    /// <summary>
    /// SNS 프로필 사진식 영역 지정 계산 (GameDesign §9). 확대 100% = 원본 안에 들어가는 가장 큰 카드 비율 영역.
    /// 영역이 원본 밖으로 나가지 않게 중심을 맞춘다.
    /// </summary>
    public static class ArtCrop
    {
        public static CropRect Compute(int sourceWidth, int sourceHeight, int aspectWidth, int aspectHeight,
            int zoomPercent, int centerX, int centerY, int maxZoomPercent)
        {
            if (sourceWidth <= 0 || sourceHeight <= 0 || aspectWidth <= 0 || aspectHeight <= 0)
                return new CropRect(0, 0, 0, 0);

            int zoom = Clamp(zoomPercent, 100, maxZoomPercent < 100 ? 100 : maxZoomPercent);

            long w, h;
            if ((long)sourceWidth * aspectHeight >= (long)sourceHeight * aspectWidth)
            {
                h = sourceHeight;
                w = (long)sourceHeight * aspectWidth / aspectHeight;
            }
            else
            {
                w = sourceWidth;
                h = (long)sourceWidth * aspectHeight / aspectWidth;
            }

            int width = (int)(w * 100 / zoom);
            int height = (int)(h * 100 / zoom);
            if (width < 1) width = 1;
            if (height < 1) height = 1;

            int x = Clamp(centerX - width / 2, 0, sourceWidth - width);
            int y = Clamp(centerY - height / 2, 0, sourceHeight - height);
            return new CropRect(x, y, width, height);
        }

        static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);
    }
}
