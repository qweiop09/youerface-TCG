using System;
using System.Security.Cryptography;
using System.Text;

namespace TCG.Core.Net
{
    /// <summary>
    /// 온라인 코인 토스 (GameDesign §11.3): 방장이 결과를 조작할 수 없게 양쪽 비밀값을 합쳐 선공을 정한다.
    /// 1) 방장: 비밀값의 해시(<see cref="Commit"/>)만 Welcome에 보낸다.
    /// 2) 게스트: 해시를 받은 뒤 자기 비밀값을 DeckSubmit에 보낸다 (방장 값을 모르므로 결과를 고를 수 없음).
    /// 3) 방장: MatchStart에 비밀값을 공개한다. 게스트는 해시와 맞는지, 선공이 <see cref="FirstSeat"/>와 같은지 확인한다.
    ///    (방장은 해시를 먼저 보냈으므로 게스트 값을 본 뒤 비밀값을 바꿀 수 없음.)
    /// 비밀값은 추측할 수 없어야 해서 암호용 난수로 만든다. 게임 규칙 난수(<see cref="Random.DeterministicRng"/>)와는 무관하고,
    /// 결과(선공 자리)만 <see cref="Rules.GameEngine.StartGame"/>에 넘긴다.
    /// </summary>
    public static class CoinToss
    {
        /// <summary>비밀값 최대 길이. 넘거나 비어 있으면 잘못된 값으로 본다.</summary>
        public const int MaxSecretLength = 64;

        /// <summary>새 비밀값 (16바이트 = 소문자 16진수 32자).</summary>
        public static string NewSecret()
        {
            var bytes = new byte[16];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(bytes);
            return ToHex(bytes);
        }

        public static bool IsValidSecret(string secret) => !string.IsNullOrEmpty(secret) && secret.Length <= MaxSecretLength;

        /// <summary>비밀값을 미리 약속하는 해시 (공개 전에 보냄).</summary>
        public static string Commit(string secret) => ToHex(Sha256("coin-commit:" + secret));

        public static bool Verify(string commit, string secret) =>
            IsValidSecret(secret) && commit != null && string.Equals(commit, Commit(secret), StringComparison.Ordinal);

        /// <summary>선공 자리 (0 = 방장, 1 = 게스트). 양쪽 값 중 하나라도 무작위면 결과도 무작위.</summary>
        public static int FirstSeat(string hostSecret, string guestSecret)
        {
            var hash = Sha256("coin-toss:" + hostSecret + "|" + guestSecret);
            return hash[0] & 1;
        }

        static byte[] Sha256(string text)
        {
            using (var sha = SHA256.Create())
                return sha.ComputeHash(Encoding.UTF8.GetBytes(text));
        }

        static string ToHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++)
                sb.Append(bytes[i].ToString("x2"));
            return sb.ToString();
        }
    }
}
