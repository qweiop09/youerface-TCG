using System.Collections.Generic;

namespace TCG.Core.Data
{
    /// <summary>
    /// 유저 에셋(카드 그림·사운드) 제한값 (GameDesign §9). 숫자는 코드에 박지 않고 여기서 읽는다.
    /// </summary>
    public sealed class AssetLimits
    {
        /// <summary>저장하는 카드 그림 크기(픽셀). 비율은 보드 카드의 그림 칸(6:5)과 같다.</summary>
        public int ArtWidth { get; set; } = 512;
        public int ArtHeight { get; set; } = 427;
        /// <summary>그림 영역 지정 시 최대 확대(백분율, 100 = 원본에 꽉 맞춤).</summary>
        public int MaxArtZoomPercent { get; set; } = 400;
        /// <summary>저장 JPG 품질(1~100).</summary>
        public int ArtJpgQuality { get; set; } = 90;

        /// <summary>사운드 최대 길이(밀리초).</summary>
        public int MaxSoundMilliseconds { get; set; } = 5000;

        public List<string> ImageExtensions { get; set; } = new List<string> { "png", "jpg", "jpeg" };
        public List<string> SoundExtensions { get; set; } = new List<string> { "ogg", "wav" };

        /// <summary>대전 시작 때 상대에게서 받는 에셋 1개의 최대 크기(바이트). 기술 제한(규칙 아님).</summary>
        public int MaxTransferBytes { get; set; } = 4 * 1024 * 1024;

        public static AssetLimits CreateDefault() => new AssetLimits();
    }
}
