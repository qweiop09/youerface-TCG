using System;
using System.Collections.Generic;
using TCG.Core.Data;

namespace TCG.Core.Net
{
    /// <summary>에셋 저장소 (Client의 유저 에셋 저장소를 감싼다). 해시는 "sha256:…".</summary>
    public interface IAssetSource
    {
        bool Has(string hash);
        bool TryRead(string hash, out byte[] data, out string extension);
        /// <summary>저장하고 해시를 돌려준다.</summary>
        string Write(byte[] data, string extension);
    }

    /// <summary>
    /// 핸드셰이크의 에셋 교환 (GameDesign §9): 상대 덱 카드가 참조하는 에셋 중 없는 것만 요청하고, 상대 요청에는 조각으로 답한다.
    /// 받은 내용은 해시·크기·확장자를 확인한 뒤 저장한다. 상대에게도 없으면 그 카드는 기본 표시.
    /// </summary>
    public sealed class AssetExchange
    {
        readonly IAssetSource _store;
        readonly AssetLimits _limits;
        readonly HashSet<string> _waiting = new HashSet<string>();
        readonly Dictionary<string, Partial> _partials = new Dictionary<string, Partial>();

        public int Received { get; private set; }
        public int Missing { get; private set; }
        public int Rejected { get; private set; }
        public bool Requested { get; private set; }
        /// <summary>요청한 것을 다 받았거나 없다는 답을 받았다.</summary>
        public bool Done => Requested && _waiting.Count == 0;

        public AssetExchange(IAssetSource store, AssetLimits limits = null)
        {
            _store = store;
            _limits = limits ?? AssetLimits.CreateDefault();
        }

        /// <summary>상대 카드들에서 내게 없는 에셋을 골라 요청한다 (없으면 요청 메시지를 보내지 않음).</summary>
        public void RequestMissing(IReadOnlyList<CardDefinition> theirCards, ITransport transport)
        {
            Requested = true;
            var request = new AssetRequestMsg();
            for (int i = 0; i < theirCards.Count; i++)
            {
                var hashes = CardAssetRefs.Collect(theirCards[i]);
                for (int h = 0; h < hashes.Count; h++)
                {
                    string hash = hashes[h];
                    if (!AssetHash.IsValid(hash) || _store == null || _store.Has(hash) || !_waiting.Add(hash))
                        continue;
                    request.Hashes.Add(hash);
                }
            }
            if (request.Hashes.Count > 0)
                transport.Send(NetCodec.Encode(request));
        }

        /// <summary>에셋 관련 메시지면 처리하고 true.</summary>
        public bool Handle(NetMessage message, ITransport transport)
        {
            switch (message)
            {
                case AssetRequestMsg request:
                    Serve(request, transport);
                    return true;
                case AssetChunkMsg chunk:
                    Receive(chunk);
                    return true;
                case AssetMissingMsg missing:
                    if (_waiting.Remove(missing.Hash ?? ""))
                        Missing++;
                    return true;
                default:
                    return false;
            }
        }

        void Serve(AssetRequestMsg request, ITransport transport)
        {
            if (request.Hashes == null)
                return;
            for (int i = 0; i < request.Hashes.Count; i++)
            {
                string hash = request.Hashes[i];
                if (_store == null || !_store.TryRead(hash, out var data, out var extension) || data == null)
                {
                    transport.Send(NetCodec.Encode(new AssetMissingMsg { Hash = hash }));
                    continue;
                }
                int size = NetProtocol.AssetChunkBytes;
                int count = Math.Max(1, (data.Length + size - 1) / size);
                for (int c = 0; c < count; c++)
                {
                    int offset = c * size;
                    int length = Math.Min(size, data.Length - offset);
                    transport.Send(NetCodec.Encode(new AssetChunkMsg
                    {
                        Hash = hash,
                        Extension = extension,
                        Index = c,
                        Count = count,
                        TotalBytes = data.Length,
                        Data = Convert.ToBase64String(data, offset, length),
                    }));
                }
            }
        }

        void Receive(AssetChunkMsg chunk)
        {
            string hash = chunk.Hash ?? "";
            if (!_waiting.Contains(hash))
                return;
            if (!_partials.TryGetValue(hash, out var partial))
            {
                if (chunk.TotalBytes < 0 || chunk.TotalBytes > _limits.MaxTransferBytes || chunk.Count <= 0 || !AllowedExtension(chunk.Extension))
                {
                    Fail(hash);
                    return;
                }
                partial = new Partial(chunk.TotalBytes, chunk.Count, chunk.Extension);
                _partials.Add(hash, partial);
            }
            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(chunk.Data ?? "");
            }
            catch (FormatException)
            {
                Fail(hash);
                return;
            }
            if (chunk.Index != partial.NextIndex || partial.Offset + bytes.Length > partial.Data.Length)
            {
                Fail(hash);
                return;
            }
            Buffer.BlockCopy(bytes, 0, partial.Data, partial.Offset, bytes.Length);
            partial.Offset += bytes.Length;
            partial.NextIndex++;
            if (partial.NextIndex < partial.Count)
                return;

            _partials.Remove(hash);
            _waiting.Remove(hash);
            if (partial.Offset != partial.Data.Length || AssetHash.Compute(partial.Data) != hash)
            {
                Rejected++;
                return;
            }
            _store.Write(partial.Data, partial.Extension);
            Received++;
        }

        void Fail(string hash)
        {
            _partials.Remove(hash);
            if (_waiting.Remove(hash))
                Rejected++;
        }

        bool AllowedExtension(string extension)
        {
            string ext = (extension ?? "").ToLowerInvariant();
            return _limits.ImageExtensions.Contains(ext) || _limits.SoundExtensions.Contains(ext);
        }

        sealed class Partial
        {
            public readonly byte[] Data;
            public readonly int Count;
            public readonly string Extension;
            public int Offset;
            public int NextIndex;

            public Partial(int total, int count, string extension)
            {
                Data = new byte[total];
                Count = count;
                Extension = extension.ToLowerInvariant();
            }
        }
    }
}
