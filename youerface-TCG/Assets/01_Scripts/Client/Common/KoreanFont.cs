using TMPro;
using UnityEngine;

namespace TCG.Client.Common
{
    /// <summary>
    /// 한글 표시용 TextMeshPro 폰트. 지금은 운영체제에 설치된 한글 폰트를 실행 중에 불러온다.
    /// 출시 전에는 라이선스가 맞는 폰트 파일(예: Noto Sans KR)을 프로젝트에 넣어 교체한다.
    /// </summary>
    public static class KoreanFont
    {
        static readonly (string family, string style)[] Candidates =
        {
            ("Apple SD Gothic Neo", "Regular"),
            ("Apple SD Gothic Neo", "Medium"),
            ("Malgun Gothic", "Regular"),
            ("Noto Sans KR", "Regular"),
            ("Noto Sans CJK KR", "Regular"),
            ("NanumGothic", "Regular"),
        };

        static TMP_FontAsset _font;

        public static TMP_FontAsset Get()
        {
            if (_font != null)
                return _font;

            foreach (var (family, style) in Candidates)
            {
                _font = TMP_FontAsset.CreateFontAsset(family, style);
                if (_font != null)
                {
                    _font.name = $"Korean ({family})";
                    return _font;
                }
            }

            Debug.LogWarning("한글 시스템 폰트를 찾지 못했습니다. TextMeshPro 기본 폰트를 씁니다.");
            _font = TMP_Settings.defaultFontAsset;
            return _font;
        }
    }
}
