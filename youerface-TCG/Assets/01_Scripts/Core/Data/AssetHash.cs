using System;
using System.Security.Cryptography;
using System.Text;

namespace TCG.Core.Data
{
    /// <summary>에셋 내용 해시. 카드 JSON은 "sha256:소문자16진수"로 에셋을 참조한다.</summary>
    public static class AssetHash
    {
        public const string Prefix = "sha256:";

        public static string Compute(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            using (var sha = SHA256.Create())
                return Prefix + ToHex(sha.ComputeHash(data));
        }

        /// <summary>"sha256:" 뒤의 16진수 부분. 형식이 맞지 않으면 null.</summary>
        public static string HexOf(string hash)
        {
            if (hash == null || !hash.StartsWith(Prefix, StringComparison.Ordinal))
                return null;
            string hex = hash.Substring(Prefix.Length);
            if (hex.Length != 64)
                return null;
            for (int i = 0; i < hex.Length; i++)
            {
                char c = hex[i];
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
                    return null;
            }
            return hex;
        }

        public static bool IsValid(string hash) => HexOf(hash) != null;

        static string ToHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++)
                sb.Append(bytes[i].ToString("x2"));
            return sb.ToString();
        }
    }
}
