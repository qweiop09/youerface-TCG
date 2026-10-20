using System.Collections.Generic;
using TCG.Core.Data;
using UnityEngine;

namespace TCG.Client.UserAssets
{
    /// <summary>카드 사운드(등장·퇴장·공격·피격) 재생. 대전 시작 때 덱의 사운드를 미리 읽어 두고, 읽힌 것만 재생한다.</summary>
    public sealed class CardSoundPlayer : MonoBehaviour
    {
        AudioSource _source;

        void Awake()
        {
            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;
        }

        /// <summary>카드들(포함된 소환물까지)의 사운드를 읽기 시작한다.</summary>
        public void Preload(IEnumerable<CardDefinition> cards)
        {
            if (!Application.isPlaying)
                return;
            var hashes = new List<string>();
            foreach (var card in cards)
                CollectSounds(card, hashes);
            var cache = UserAssetService.Cache;
            for (int i = 0; i < hashes.Count; i++)
                StartCoroutine(cache.LoadClip(hashes[i]));
        }

        public void PlayEnter(CardDefinition card) => Play(card?.Sounds?.Enter);
        public void PlayExit(CardDefinition card) => Play(card?.Sounds?.Exit);
        public void PlayAttack(CardDefinition card) => Play(card?.Sounds?.Attack);
        public void PlayDamaged(CardDefinition card) => Play(card?.Sounds?.Damaged);

        // 카드 소리는 대기열로 낸다: 동시에 MaxConcurrent개까지 겹쳐 나고(광역 피격 등), 그보다 많으면 앞 소리가 끝날 때까지 기다린다.
        // 시작 사이에는 최소 MinGap을 둔다. 소리는 버리지 않는다 (실제 시간, 연출 배속과 무관).
        /// <summary>동시에 겹쳐 낼 수 있는 카드 소리 수.</summary>
        public const int MaxConcurrent = 3;
        /// <summary>소리 시작 사이 최소 틈.</summary>
        public const float MinGap = 0.12f;

        readonly Queue<AudioClip> _queue = new Queue<AudioClip>();
        readonly List<float> _playingUntil = new List<float>();
        float _lastStart = -1f;

        void Play(string hash)
        {
            if (_source == null)
                return;
            var clip = UserAssetService.Cache.Clip(hash);
            if (clip != null)
                _queue.Enqueue(clip);
        }

        /// <summary>
        /// 전투 연출용: 지금 바로 재생하고 길이(초)를 돌려준다 (없으면 0). 연출은 이 길이만큼 기다린 뒤 다음으로 넘어간다.
        /// </summary>
        public float PlayNow(string hash)
        {
            var clip = _source != null ? UserAssetService.Cache.Clip(hash) : null;
            if (clip == null)
                return 0f;
            StartClip(clip);
            return clip.length;
        }

        public float PlayAttackNow(CardDefinition card) => PlayNow(card?.Sounds?.Attack);
        public float PlayDamagedNow(CardDefinition card) => PlayNow(card?.Sounds?.Damaged);

        void StartClip(AudioClip clip)
        {
            _source.PlayOneShot(clip);
            _lastStart = Time.unscaledTime;
            _playingUntil.Add(Time.unscaledTime + clip.length);
        }

        void Update()
        {
            float now = Time.unscaledTime;
            _playingUntil.RemoveAll(t => t <= now);
            while (_queue.Count > 0 && _playingUntil.Count < MaxConcurrent && now - _lastStart >= MinGap)
            {
                var clip = _queue.Dequeue();
                if (clip != null)
                    StartClip(clip);
            }
        }

        static void CollectSounds(CardDefinition card, List<string> result)
        {
            if (card == null)
                return;
            Add(card.Sounds?.Enter, result);
            Add(card.Sounds?.Exit, result);
            Add(card.Sounds?.Attack, result);
            Add(card.Sounds?.Damaged, result);
            if (card.Effects == null)
                return;
            for (int i = 0; i < card.Effects.Count; i++)
                CollectSounds(card.Effects[i]?.Action?.Summon, result);
        }

        static void Add(string hash, List<string> result)
        {
            if (!string.IsNullOrEmpty(hash) && !result.Contains(hash))
                result.Add(hash);
        }
    }
}
