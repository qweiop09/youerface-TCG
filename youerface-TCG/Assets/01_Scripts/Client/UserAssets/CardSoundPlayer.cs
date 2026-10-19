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

        void Play(string hash)
        {
            if (_source == null)
                return;
            var clip = UserAssetService.Cache.Clip(hash);
            if (clip != null)
                _source.PlayOneShot(clip);
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
