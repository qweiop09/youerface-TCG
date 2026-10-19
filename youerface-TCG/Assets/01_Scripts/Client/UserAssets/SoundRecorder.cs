using System;
using System.Collections;
using UnityEngine;

namespace TCG.Client.UserAssets
{
    /// <summary>
    /// 마이크 녹음 (카드 제작기에서 사운드를 바로 녹음). 최대 길이(<see cref="TCG.Core.Data.AssetLimits.MaxSoundMilliseconds"/>)가
    /// 되면 저절로 멈춘다. 멈추면 녹음한 만큼만 WAV로 바꿔 저장소에 넣고 해시를 돌려준다.
    /// </summary>
    public sealed class SoundRecorder : MonoBehaviour
    {
        const int Frequency = 44100;

        AudioClip _clip;
        string _device;
        float _startedAt;
        Action<string, string> _done;

        public bool IsRecording => _clip != null;
        /// <summary>녹음한 시간(초).</summary>
        public float Elapsed => IsRecording ? Time.unscaledTime - _startedAt : 0f;
        public float MaxSeconds => UserAssetService.Limits.MaxSoundMilliseconds / 1000f;

        /// <summary>녹음을 시작한다 (마이크 권한을 먼저 묻는다). done(해시, 오류)는 멈춘 뒤 한 번 불린다.</summary>
        public void Begin(Action<string, string> done)
        {
            if (IsRecording)
                return;
            StartCoroutine(BeginRoutine(done));
        }

        IEnumerator BeginRoutine(Action<string, string> done)
        {
            if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
                yield return Application.RequestUserAuthorization(UserAuthorization.Microphone);
            if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
            {
                done(null, "마이크 사용이 허용되지 않았습니다. 시스템 설정에서 마이크 권한을 켜 주세요.");
                yield break;
            }
            if (Microphone.devices.Length == 0)
            {
                done(null, "마이크를 찾을 수 없습니다.");
                yield break;
            }

            _device = Microphone.devices[0];
            int seconds = Mathf.CeilToInt(MaxSeconds);
            _clip = Microphone.Start(_device, false, seconds, Frequency);
            if (_clip == null)
            {
                done(null, "녹음을 시작할 수 없습니다.");
                yield break;
            }
            _startedAt = Time.unscaledTime;
            _done = done;
        }

        void Update()
        {
            // 최대 길이가 되면 저절로 멈춘다.
            if (IsRecording && (Elapsed >= MaxSeconds || !Microphone.IsRecording(_device)))
                Stop();
        }

        /// <summary>녹음을 멈추고 저장한다.</summary>
        public void Stop()
        {
            if (!IsRecording)
                return;
            int position = Microphone.GetPosition(_device);
            Microphone.End(_device);
            var clip = _clip;
            var done = _done;
            _clip = null;
            _done = null;

            int maxSamples = Mathf.FloorToInt(MaxSeconds * clip.frequency);
            // 끝까지 녹음된 경우 GetPosition이 0을 돌려줄 수 있다.
            int samples = position > 0 ? Mathf.Min(position, maxSamples) : Mathf.Min(clip.samples, maxSamples);
            if (samples < clip.frequency / 10)
            {
                Destroy(clip);
                done?.Invoke(null, "녹음이 너무 짧습니다.");
                return;
            }
            byte[] wav = WavEncoder.Encode(clip, samples);
            Destroy(clip);
            done?.Invoke(UserAssetService.Store.Put(wav, "wav"), null);
        }

        /// <summary>저장하지 않고 버린다 (화면을 떠날 때).</summary>
        public void Cancel()
        {
            if (!IsRecording)
                return;
            Microphone.End(_device);
            Destroy(_clip);
            _clip = null;
            _done = null;
        }

        void OnDisable() => Cancel();
    }
}
