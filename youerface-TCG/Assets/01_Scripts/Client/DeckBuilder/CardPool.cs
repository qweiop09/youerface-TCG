using System.Collections.Generic;
using TCG.Client.Battle;
using TCG.Client.CardMaker;
using TCG.Core.Data;
using UnityEngine;

namespace TCG.Client.DeckBuilder
{
    /// <summary>
    /// 덱에 넣을 수 있는 카드 전체: 내 카드(<see cref="LocalCardLibrary"/>, 최근 수정 순) + 샘플 카드(Resources, ID 순).
    /// 덱 편집 화면과 시작 화면(덱 선택 → 대전)이 같은 모음으로 덱을 해석한다.
    /// </summary>
    public sealed class CardPool
    {
        readonly List<string> _samples = new List<string>();

        public CardCatalog Catalog { get; } = new CardCatalog();
        public IReadOnlyList<CardDefinition> Cards => Catalog.Cards;
        /// <summary>최근 수정 시각 (내 카드만, 정렬용).</summary>
        public Dictionary<string, System.DateTime> Modified { get; } = new Dictionary<string, System.DateTime>();

        public bool IsSample(string id) => _samples.Contains(id);

        public static CardPool Load(LocalCardLibrary cards)
        {
            var pool = new CardPool();
            if (cards != null)
            {
                foreach (var entry in cards.List())
                {
                    if (!TryAdd(pool, entry.Card))
                        continue;
                    pool.Modified[entry.Card.Id] = entry.Modified;
                }
            }
            foreach (var card in SampleData.LoadCatalog().Cards)
            {
                if (TryAdd(pool, card))
                    pool._samples.Add(card.Id);
            }
            return pool;
        }

        static bool TryAdd(CardPool pool, CardDefinition card)
        {
            if (string.IsNullOrEmpty(card?.Id) || pool.Catalog.TryGet(card.Id, out _))
            {
                Debug.LogWarning($"카드 ID가 없거나 겹쳐서 건너뜁니다: {card?.Id}");
                return false;
            }
            pool.Catalog.Add(card);
            return true;
        }
    }
}
