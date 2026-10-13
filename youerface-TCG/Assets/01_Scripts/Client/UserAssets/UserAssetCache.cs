using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TCG.Client.UserAssets
{
    /// <summary>저장소의 에셋을 실행 중에 읽어 둔다 (해시 → Texture2D / AudioClip). 없는 해시는 null.</summary>
    public sealed class UserAssetCache
    {
        readonly IUserAssetStore _store;
        readonly Dictionary<string, Texture2D> _textures = new Dictionary<string, Texture2D>();
        readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        readonly HashSet<string> _loadingClips = new HashSet<string>();

        public UserAssetCache(IUserAssetStore store) => _store = store;

        public Texture2D Texture(string hash)
        {
            if (string.IsNullOrEmpty(hash))
                return null;
            if (_textures.TryGetValue(hash, out var cached))
                return cached;

            Texture2D texture = null;
            var bytes = _store.Load(hash);
            if (bytes != null)
            {
                texture = new Texture2D(2, 2, TextureFormat.RGB24, false) { name = hash, wrapMode = TextureWrapMode.Clamp };
                if (!texture.LoadImage(bytes, true))
                {
                    Object.Destroy(texture);
                    texture = null;
                }
            }
            _textures[hash] = texture;
            return texture;
        }

        /// <summary>이미 읽어 둔 사운드. 없으면 null (읽기는 <see cref="LoadClip"/>).</summary>
        public AudioClip Clip(string hash) =>
            !string.IsNullOrEmpty(hash) && _clips.TryGetValue(hash, out var clip) ? clip : null;

        public bool HasClipLoaded(string hash) => !string.IsNullOrEmpty(hash) && _clips.ContainsKey(hash);

        public IEnumerator LoadClip(string hash)
        {
            if (string.IsNullOrEmpty(hash) || _clips.ContainsKey(hash) || !_loadingClips.Add(hash))
                yield break;
            string path = _store.PathOf(hash);
            AudioClip clip = null;
            if (path != null)
                yield return UserAssetImporter.LoadClip(path, c => clip = c, null);
            _clips[hash] = clip;
            _loadingClips.Remove(hash);
        }
    }
}
