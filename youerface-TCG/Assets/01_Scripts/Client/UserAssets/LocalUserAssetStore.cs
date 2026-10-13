using System.IO;
using TCG.Core.Data;
using UnityEngine;

namespace TCG.Client.UserAssets
{
    /// <summary>persistentDataPath/UserAssets/&lt;16진수 해시&gt;.&lt;확장자&gt; 로 저장한다.</summary>
    public sealed class LocalUserAssetStore : IUserAssetStore
    {
        readonly string _root;

        public LocalUserAssetStore(string root = null)
        {
            _root = root ?? Path.Combine(Application.persistentDataPath, "UserAssets");
            Directory.CreateDirectory(_root);
        }

        public string Root => _root;

        public string Put(byte[] data, string extension)
        {
            string hash = AssetHash.Compute(data);
            if (PathOf(hash) == null)
            {
                string ext = string.IsNullOrEmpty(extension) ? "bin" : extension.TrimStart('.').ToLowerInvariant();
                string path = Path.Combine(_root, AssetHash.HexOf(hash) + "." + ext);
                string temp = path + ".tmp";
                File.WriteAllBytes(temp, data);
                File.Move(temp, path);
            }
            return hash;
        }

        public bool Contains(string hash) => PathOf(hash) != null;

        public string PathOf(string hash)
        {
            string hex = AssetHash.HexOf(hash);
            if (hex == null || !Directory.Exists(_root))
                return null;
            var files = Directory.GetFiles(_root, hex + ".*");
            for (int i = 0; i < files.Length; i++)
            {
                if (!files[i].EndsWith(".tmp"))
                    return files[i];
            }
            return null;
        }

        public byte[] Load(string hash)
        {
            string path = PathOf(hash);
            return path == null ? null : File.ReadAllBytes(path);
        }
    }
}
