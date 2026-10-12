using System;
using System.Collections.Generic;

namespace TCG.Core.Random
{
    /// <summary>
    /// 플랫폼과 무관하게 같은 시드에서 같은 수열을 내는 난수기 (xoshiro128**, 시드 확장은 splitmix32).
    /// 게임 로직의 난수는 전부 이것만 쓴다. 상태를 저장·복원할 수 있어 리플레이와 동기화 검증에 쓴다.
    /// 알고리즘을 바꾸면 기존 리플레이가 깨지므로 테스트의 고정값으로 보호한다.
    /// </summary>
    public sealed class DeterministicRng
    {
        uint _s0, _s1, _s2, _s3;

        public DeterministicRng(int seed)
        {
            uint z = unchecked((uint)seed);
            _s0 = SplitMix32(ref z);
            _s1 = SplitMix32(ref z);
            _s2 = SplitMix32(ref z);
            _s3 = SplitMix32(ref z);
            if ((_s0 | _s1 | _s2 | _s3) == 0)
                _s0 = 1;
        }

        public DeterministicRng(RngState state)
        {
            RestoreState(state);
        }

        public RngState SaveState() => new RngState(_s0, _s1, _s2, _s3);

        public void RestoreState(RngState state)
        {
            if ((state.S0 | state.S1 | state.S2 | state.S3) == 0)
                throw new ArgumentException("RNG state must not be all zero.", nameof(state));
            _s0 = state.S0;
            _s1 = state.S1;
            _s2 = state.S2;
            _s3 = state.S3;
        }

        public uint NextUInt()
        {
            unchecked
            {
                uint result = RotateLeft(_s1 * 5, 7) * 9;
                uint t = _s1 << 9;
                _s2 ^= _s0;
                _s3 ^= _s1;
                _s1 ^= _s2;
                _s0 ^= _s3;
                _s2 ^= t;
                _s3 = RotateLeft(_s3, 11);
                return result;
            }
        }

        /// <summary>[0, maxExclusive) 범위의 균등 난수.</summary>
        public int Next(int maxExclusive)
        {
            if (maxExclusive <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxExclusive));

            uint bound = (uint)maxExclusive;
            // 나머지 연산의 편향을 없애기 위한 거부 샘플링.
            uint threshold = unchecked(0u - bound) % bound;
            while (true)
            {
                uint r = NextUInt();
                if (r >= threshold)
                    return (int)(r % bound);
            }
        }

        /// <summary>[minInclusive, maxExclusive) 범위의 균등 난수.</summary>
        public int Next(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
                throw new ArgumentOutOfRangeException(nameof(maxExclusive));
            long range = (long)maxExclusive - minInclusive;
            if (range > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(maxExclusive), "Range too large.");
            return minInclusive + Next((int)range);
        }

        /// <summary>Fisher–Yates 셔플.</summary>
        public void Shuffle<T>(IList<T> list)
        {
            if (list == null)
                throw new ArgumentNullException(nameof(list));
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Next(i + 1);
                T tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
        }

        static uint RotateLeft(uint x, int k) => (x << k) | (x >> (32 - k));

        static uint SplitMix32(ref uint z)
        {
            unchecked
            {
                z += 0x9E3779B9u;
                uint r = z;
                r = (r ^ (r >> 16)) * 0x85EBCA6Bu;
                r = (r ^ (r >> 13)) * 0xC2B2AE35u;
                return r ^ (r >> 16);
            }
        }
    }

    /// <summary><see cref="DeterministicRng"/>의 내부 상태. GameState와 함께 저장·전송한다.</summary>
    public readonly struct RngState : IEquatable<RngState>
    {
        public uint S0 { get; }
        public uint S1 { get; }
        public uint S2 { get; }
        public uint S3 { get; }

        public RngState(uint s0, uint s1, uint s2, uint s3)
        {
            S0 = s0;
            S1 = s1;
            S2 = s2;
            S3 = s3;
        }

        public bool Equals(RngState other) => S0 == other.S0 && S1 == other.S1 && S2 == other.S2 && S3 == other.S3;
        public override bool Equals(object obj) => obj is RngState other && Equals(other);
        public override int GetHashCode() => unchecked((int)(S0 ^ (S1 * 31) ^ (S2 * 961) ^ (S3 * 29791)));
        public override string ToString() => $"{S0:X8}-{S1:X8}-{S2:X8}-{S3:X8}";
    }
}
