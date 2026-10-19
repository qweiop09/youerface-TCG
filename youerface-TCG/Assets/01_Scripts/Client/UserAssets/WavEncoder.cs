using System;
using System.IO;
using UnityEngine;

namespace TCG.Client.UserAssets
{
    /// <summary>AudioClip → WAV(16비트 PCM) 바이트. 녹음한 소리를 다른 사운드와 같은 형식(WAV)으로 저장하는 데 쓴다.</summary>
    public static class WavEncoder
    {
        public static byte[] Encode(AudioClip clip, int sampleCount)
        {
            int channels = clip.channels;
            sampleCount = Mathf.Clamp(sampleCount, 0, clip.samples);
            var samples = new float[sampleCount * channels];
            if (sampleCount > 0)
                clip.GetData(samples, 0);

            int dataBytes = samples.Length * 2;
            using (var stream = new MemoryStream(44 + dataBytes))
            using (var w = new BinaryWriter(stream))
            {
                w.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
                w.Write(36 + dataBytes);
                w.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E' });
                w.Write(new[] { (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
                w.Write(16);                               // fmt 크기
                w.Write((short)1);                         // PCM
                w.Write((short)channels);
                w.Write(clip.frequency);
                w.Write(clip.frequency * channels * 2);    // 초당 바이트
                w.Write((short)(channels * 2));            // 블록 정렬
                w.Write((short)16);                        // 비트
                w.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
                w.Write(dataBytes);
                for (int i = 0; i < samples.Length; i++)
                    w.Write((short)Math.Round(Mathf.Clamp(samples[i], -1f, 1f) * short.MaxValue));
                return stream.ToArray();
            }
        }
    }
}
