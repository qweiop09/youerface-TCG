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

    /// <summary>덱 규칙 검사 (장수·같은 카드 매수·카드 유효성). 빈 목록이면 유효.</summary>
    public static class DeckValidator
    {
        public static IReadOnlyList<string> Validate(DeckDefinition deck, CardCatalog catalog, RuleConfig rules)
        {
            var errors = new List<string>();
            if (deck?.Cards == null)
            {
                errors.Add("덱이 비어 있습니다.");
                return errors;
            }

            if (deck.Cards.Count != rules.DeckSize)
                errors.Add($"덱은 {rules.DeckSize}장이어야 합니다 (현재 {deck.Cards.Count}장).");

            var counted = new List<string>();
            for (int i = 0; i < deck.Cards.Count; i++)
            {
                string id = deck.Cards[i];
                if (counted.Contains(id))
                    continue;
                counted.Add(id);

                int copies = 0;
                for (int k = 0; k < deck.Cards.Count; k++)
                {
                    if (deck.Cards[k] == id)
                        copies++;
                }
                if (copies > rules.MaxCopiesPerCard)
                    errors.Add($"'{id}'는 {rules.MaxCopiesPerCard}장까지 넣을 수 있습니다 (현재 {copies}장).");

                if (!catalog.TryGet(id, out var card))
                {
                    errors.Add($"없는 카드입니다: {id}");
                    continue;
                }
                var cardErrors = CardValidator.Validate(card);
                for (int e = 0; e < cardErrors.Count; e++)
                    errors.Add($"{id}: {cardErrors[e]}");
            }
            return errors;
        }
    }
}
