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

        // 전투처럼 공격음 → 피격음 → 사망음(퇴장음)이 한꺼번에 나오면 서로 묻히므로, 카드 소리는 대기열로 차례로 낸다.
        // 다음 소리는 앞 소리가 시작한 뒤 min(앞 소리 길이, MaxSpacing) + Gap 이 지나야 시작한다 (실제 시간, 연출 배속과 무관).
        /// <summary>앞 소리를 기다리는 최대 시간 (긴 소리 때문에 다음 소리가 너무 늦어지지 않게).</summary>
        public const float MaxSpacing = 0.6f;
        /// <summary>소리 사이 틈.</summary>
        public const float Gap = 0.12f;
        /// <summary>이보다 오래 밀린 소리는 버린다 (연출과 너무 어긋나지 않게).</summary>
        const float MaxQueueDelay = 2f;

        readonly Queue<(AudioClip clip, float queuedAt)> _queue = new Queue<(AudioClip, float)>();
        float _nextAllowed;

        void Play(string hash)
        {
            if (_source == null)
                return;
            var clip = UserAssetService.Cache.Clip(hash);
            if (clip != null)
                _queue.Enqueue((clip, Time.unscaledTime));
        }

        void Update()
        {
            while (_queue.Count > 0 && Time.unscaledTime >= _nextAllowed)
            {
                var (clip, queuedAt) = _queue.Dequeue();
                if (clip == null || Time.unscaledTime - queuedAt > MaxQueueDelay)
                    continue;
                _source.PlayOneShot(clip);
                _nextAllowed = Time.unscaledTime + Mathf.Min(clip.length, MaxSpacing) + Gap;
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
