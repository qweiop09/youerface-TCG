using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using NUnit.Framework;
using TCG.Core.Random;

namespace TCG.Tests.Core
{
    public class DeterministicRngTests
    {
        static uint[] Take(DeterministicRng rng, int count)
        {
            var values = new uint[count];
            for (int i = 0; i < count; i++)
                values[i] = rng.NextUInt();
            return values;
        }

        [Test]
        public void SameSeed_ProducesSameSequence()
        {
            Assert.That(Take(new DeterministicRng(12345), 100), Is.EqualTo(Take(new DeterministicRng(12345), 100)));
        }

        [Test]
        public void DifferentSeed_ProducesDifferentSequence()
        {
            Assert.That(Take(new DeterministicRng(1), 10), Is.Not.EqualTo(Take(new DeterministicRng(2), 10)));
        }

        /// <summary>
        /// 알고리즘이 바뀌면 저장된 리플레이·동기화가 깨진다. 이 고정값이 바뀌면 의도한 변경인지 확인할 것.
        /// </summary>
        [Test]
        public void KnownSeed_ProducesPinnedValues()
        {
            var rng = new DeterministicRng(0);
            Assert.That(Take(rng, 4), Is.EqualTo(PinnedSeed0));

            rng = new DeterministicRng(-1);
            Assert.That(Take(rng, 4), Is.EqualTo(PinnedSeedMinus1));
        }

        static readonly uint[] PinnedSeed0 = { 3809008728u, 1133695204u, 53579671u, 2891528803u };
        static readonly uint[] PinnedSeedMinus1 = { 835879718u, 1921286648u, 2356205009u, 1885780724u };

        [Test]
        public void SaveAndRestoreState_ContinuesSameSequence()
        {
            var rng = new DeterministicRng(777);
            Take(rng, 37);
            var saved = rng.SaveState();
            var expected = Take(rng, 50);

            var restored = new DeterministicRng(saved);
            Assert.That(Take(restored, 50), Is.EqualTo(expected));

            rng.RestoreState(saved);
            Assert.That(Take(rng, 50), Is.EqualTo(expected));
        }

        [Test]
        public void State_RoundTripsThroughJson()
        {
            var rng = new DeterministicRng(99);
            Take(rng, 5);
            var saved = rng.SaveState();

            var json = JsonConvert.SerializeObject(saved);
            var loaded = JsonConvert.DeserializeObject<RngState>(json);

            Assert.That(loaded, Is.EqualTo(saved));
            Assert.That(Take(new DeterministicRng(loaded), 20), Is.EqualTo(Take(new DeterministicRng(saved), 20)));
        }

        [Test]
        public void RestoreState_AllZero_Throws()
        {
            Assert.Throws<ArgumentException>(() => new DeterministicRng(new RngState(0, 0, 0, 0)));
        }

        [Test]
        public void Next_StaysInRange()
        {
            var rng = new DeterministicRng(5);
            for (int i = 0; i < 10000; i++)
            {
                int v = rng.Next(7);
                Assert.That(v, Is.InRange(0, 6));
                int w = rng.Next(-3, 4);
                Assert.That(w, Is.InRange(-3, 3));
            }
        }

        [Test]
        public void Next_CoversAllValues()
        {
            var rng = new DeterministicRng(5);
            var counts = new int[6];
            for (int i = 0; i < 6000; i++)
                counts[rng.Next(6)]++;
            Assert.That(counts.All(c => c > 800), Is.True, string.Join(",", counts));
        }

        [Test]
        public void Next_InvalidRange_Throws()
        {
            var rng = new DeterministicRng(5);
            Assert.Throws<ArgumentOutOfRangeException>(() => rng.Next(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => rng.Next(3, 3));
        }

        [Test]
        public void Shuffle_IsReproducibleAndKeepsElements()
        {
            var a = Enumerable.Range(0, 30).ToList();
            var b = Enumerable.Range(0, 30).ToList();
            new DeterministicRng(2024).Shuffle(a);
            new DeterministicRng(2024).Shuffle(b);

            Assert.That(a, Is.EqualTo(b));
            Assert.That(a, Is.Not.EqualTo(Enumerable.Range(0, 30).ToList()));
            Assert.That(a.OrderBy(x => x), Is.EqualTo(Enumerable.Range(0, 30)));
        }

        [Test]
        public void GameState_RngIsSeeded()
        {
            var s1 = new TCG.Core.State.GameState(TCG.Core.Data.RuleConfig.CreateDefault(), seed: 31);
            var s2 = new TCG.Core.State.GameState(TCG.Core.Data.RuleConfig.CreateDefault(), seed: 31);
            Assert.That(s1.Rng.SaveState(), Is.EqualTo(s2.Rng.SaveState()));
            Assert.That(Take(s1.Rng, 10), Is.EqualTo(Take(s2.Rng, 10)));
        }
    }
}
