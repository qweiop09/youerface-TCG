using System;
using System.Collections.Generic;
using UnityEngine;

namespace TCG.Client.UserAssets
{
    /// <summary>
    /// OS 기본 파일 창 (Windows/Mac/Linux, StandaloneFileBrowser 패키지).
    /// 네이티브 플러그인을 못 불러오면 에디터에서는 Unity 파일 창으로 대신한다.
    /// </summary>
    public sealed class StandaloneFilePicker : IFilePicker
    {
        bool _nativeFailed;

        public string PickFile(string title, string filterName, IReadOnlyList<string> extensions)
        {
#if UNITY_STANDALONE || UNITY_EDITOR
            if (!_nativeFailed)
            {
                try
                {
                    var filter = new[] { new SFB.ExtensionFilter(filterName, ToArray(extensions)) };
                    var paths = SFB.StandaloneFileBrowser.OpenFilePanel(title, "", filter, false);
                    return paths != null && paths.Length > 0 && !string.IsNullOrEmpty(paths[0]) ? paths[0] : null;
                }
                catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException || e is TypeInitializationException)
                {
                    _nativeFailed = true;
                    Debug.LogWarning($"OS 파일 창을 열 수 없어 대체 창을 씁니다: {e.Message}");
                }
            }
#endif
#if UNITY_EDITOR
            string path = UnityEditor.EditorUtility.OpenFilePanelWithFilters(title, "", new[] { filterName, string.Join(",", ToArray(extensions)) });
            return string.IsNullOrEmpty(path) ? null : path;
#else
            return null;
#endif
        }

        static string[] ToArray(IReadOnlyList<string> list)
        {
            var array = new string[list.Count];
            for (int i = 0; i < list.Count; i++)
                array[i] = list[i];
            return array;
        }
    }
}
