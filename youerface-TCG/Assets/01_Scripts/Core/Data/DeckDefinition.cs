using System.Collections.Generic;

namespace TCG.Core.Data
{
    /// <summary>덱: 카드 ID 목록. 같은 카드는 ID를 여러 번 적는다.</summary>
    public sealed class DeckDefinition
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public List<string> Cards { get; set; } = new List<string>();
    }

    /// <summary>카드 ID → 카드 정의 모음. 추가한 순서를 유지한다.</summary>
    public sealed class CardCatalog
    {
        readonly Dictionary<string, CardDefinition> _byId = new Dictionary<string, CardDefinition>();
        readonly List<CardDefinition> _ordered = new List<CardDefinition>();

        public IReadOnlyList<CardDefinition> Cards => _ordered;

        public void Add(CardDefinition card)
        {
            if (string.IsNullOrEmpty(card?.Id))
                throw new System.ArgumentException("Card needs an id.");
            if (_byId.ContainsKey(card.Id))
                throw new System.ArgumentException($"Duplicate card id: {card.Id}");
            _byId.Add(card.Id, card);
            _ordered.Add(card);
        }

        public bool TryGet(string id, out CardDefinition card) => _byId.TryGetValue(id ?? "", out card);

        /// <summary>덱의 카드 ID들을 정의로 바꾼다. 없는 ID는 건너뛴다(검증은 <see cref="DeckValidator"/>).</summary>
        public List<CardDefinition> Resolve(DeckDefinition deck)
        {
            var result = new List<CardDefinition>();
            for (int i = 0; i < deck.Cards.Count; i++)
            {
                if (TryGet(deck.Cards[i], out var card))
                    result.Add(card);
            }
            return result;
        }
    }
}
