using System;
using System.Collections;
using System.IO;
using TCG.Core.Data;
using UnityEngine;
using UnityEngine.Networking;

namespace TCG.Client.UserAssets
{
    /// <summary>
    /// 파일을 검사해 저장소에 넣는다 (GameDesign §9).
    /// 이미지: 크기 제한 없이 읽고, 유저가 정한 영역만 카드 그림 크기로 잘라 JPG로 저장. 사운드: OGG/WAV, 길이 제한.
    /// </summary>
    public sealed class UserAssetImporter
    {
        readonly IUserAssetStore _store;
        readonly AssetLimits _limits;

        public UserAssetImporter(IUserAssetStore store, AssetLimits limits)
        {
            _store = store;
            _limits = limits;
        }

        /// <summary>원본 이미지를 읽는다. 실패하면 null과 오류 문구.</summary>
        public Texture2D LoadSourceImage(string path, out string error)
        {
            error = null;
            if (!HasExtension(path, _limits.ImageExtensions))
            {
                error = "PNG·JPG 이미지만 가져올 수 있습니다.";
                return null;
            }
            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (Exception e)
            {
                error = "파일을 읽을 수 없습니다: " + e.Message;
                return null;
            }

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = Path.GetFileName(path) };
            if (!texture.LoadImage(bytes, false))
            {
                UnityEngine.Object.Destroy(texture);
                error = "이미지를 읽을 수 없습니다.";
                return null;
            }
            int max = SystemInfo.maxTextureSize;
            if (texture.width > max || texture.height > max)
            {
                UnityEngine.Object.Destroy(texture);
                error = $"이미지가 너무 큽니다 (가로·세로 {max}px 이하).";
                return null;
            }
            return texture;
        }

        /// <summary>원본에서 영역을 잘라 카드 그림 크기로 줄이고 JPG로 저장한다. 좌표는 텍스처 기준(왼쪽 아래 원점).</summary>
        public string SaveArt(Texture2D source, CropRect crop)
        {
            int w = _limits.ArtWidth, h = _limits.ArtHeight;
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var scale = new Vector2((float)crop.Width / source.width, (float)crop.Height / source.height);
            var offset = new Vector2((float)crop.X / source.width, (float)crop.Y / source.height);
            var previousFilter = source.filterMode;
            source.filterMode = FilterMode.Bilinear;
            Graphics.Blit(source, rt, scale, offset);
            source.filterMode = previousFilter;

            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            var result = new Texture2D(w, h, TextureFormat.RGB24, false);
            result.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            result.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);

            byte[] jpg = result.EncodeToJPG(_limits.ArtJpgQuality);
            UnityEngine.Object.Destroy(result);
            return _store.Put(jpg, "jpg");
        }

        /// <summary>사운드를 검사하고 저장한다. 끝나면 (해시, 오류) 중 하나가 채워져 불린다.</summary>
        public IEnumerator ImportSound(string path, Action<string, string> done)
        {
            if (!HasExtension(path, _limits.SoundExtensions))
            {
                done(null, "OGG·WAV 사운드만 가져올 수 있습니다.");
                yield break;
            }

            AudioClip clip = null;
            string error = null;
            yield return LoadClip(path, c => clip = c, e => error = e);
            if (clip == null)
            {
                done(null, error ?? "사운드를 읽을 수 없습니다.");
                yield break;
            }

            int ms = (int)(clip.length * 1000f);
            UnityEngine.Object.Destroy(clip);
            if (ms > _limits.MaxSoundMilliseconds)
            {
                done(null, $"사운드는 {_limits.MaxSoundMilliseconds / 1000f:0.#}초 이하만 가져올 수 있습니다 (지금 {ms / 1000f:0.0}초).");
                yield break;
            }

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (Exception e)
            {
                done(null, "파일을 읽을 수 없습니다: " + e.Message);
                yield break;
            }
            done(_store.Put(bytes, Path.GetExtension(path).TrimStart('.')), null);
        }

        /// <summary>파일에서 AudioClip을 읽는다 (확장자로 형식 판단).</summary>
        public static IEnumerator LoadClip(string path, Action<AudioClip> onLoaded, Action<string> onError)
        {
            var type = AudioTypeOf(path);
            if (type == AudioType.UNKNOWN)
            {
                onError?.Invoke("지원하지 않는 사운드 형식입니다.");
                yield break;
            }
            using (var request = UnityWebRequestMultimedia.GetAudioClip(new Uri(path).AbsoluteUri, type))
            {
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke("사운드를 읽을 수 없습니다: " + request.error);
                    yield break;
                }
                var clip = DownloadHandlerAudioClip.GetContent(request);
                if (clip == null || clip.length <= 0f)
                {
                    onError?.Invoke("사운드를 읽을 수 없습니다.");
                    yield break;
                }
                clip.name = Path.GetFileName(path);
                onLoaded(clip);
            }
        }

        static AudioType AudioTypeOf(string path)
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".ogg": return AudioType.OGGVORBIS;
                case ".wav": return AudioType.WAV;
                default: return AudioType.UNKNOWN;
            }
        }

        static bool HasExtension(string path, System.Collections.Generic.List<string> extensions)
        {
            string ext = Path.GetExtension(path ?? "").TrimStart('.').ToLowerInvariant();
            return extensions.Contains(ext);
        }
    }
}
