using System.Collections.Generic;

namespace TCG.Client.UserAssets
{
    /// <summary>파일 하나 고르기 (이미지·사운드 가져올 때). 플랫폼별 구현을 교체한다 (Architecture §7).</summary>
    public interface IFilePicker
    {
        /// <summary>고른 파일의 전체 경로. 취소하면 null.</summary>
        string PickFile(string title, string filterName, IReadOnlyList<string> extensions);
    }
}
