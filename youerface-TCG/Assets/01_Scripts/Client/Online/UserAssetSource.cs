using System.IO;
using TCG.Client.UserAssets;
using TCG.Core.Net;

namespace TCG.Client.Online
{
    /// <summary>온라인 에셋 교환(<see cref="AssetExchange"/>)용으로 유저 에셋 저장소를 감싼다.</summary>
    public sealed class UserAssetSource : IAssetSource
    {
        readonly IUserAssetStore _store;

        public UserAssetSource(IUserAssetStore store)
        {
            _store = store;
        }

        public bool Has(string hash) => _store.Contains(hash);

        public bool TryRead(string hash, out byte[] data, out string extension)
        {
            string path = _store.PathOf(hash);
            data = path != null ? _store.Load(hash) : null;
            extension = path != null ? Path.GetExtension(path).TrimStart('.').ToLowerInvariant() : null;
            return data != null;
        }

        public string Write(byte[] data, string extension) => _store.Put(data, extension);
    }
}
