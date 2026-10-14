using System.Collections.Generic;
using System.Runtime.CompilerServices;
using TCG.Core.Data;

namespace TCG.Core.Net
{
    /// <summary>
    /// 대전에 나오는 카드 정의 목록 (덱 카드 + 그 안의 소환물). 시야 상태는 카드를 이 목록의 번호로 가리킨다.
    /// 호스트는 객체 참조로 번호를 찾는다(같은 ID라도 양쪽 카드 내용이 다를 수 있어서). 추가만 한다.
    /// </summary>
    public sealed class MatchCatalog
    {
        readonly List<CardDefinition> _cards = new List<CardDefinition>();
        readonly Dictionary<CardDefinition, int> _index = new Dictionary<CardDefinition, int>(ReferenceComparer.Instance);

        public IReadOnlyList<CardDefinition> Cards => _cards;
        public int Count => _cards.Count;

        public CardDefinition this[int index] => _cards[index];

        /// <summary>정의와 그 안의 소환물을 (처음이면) 추가하고 번호를 돌려준다.</summary>
        public int Add(CardDefinition card)
        {
            if (_index.TryGetValue(card, out int index))
                return index;
            index = _cards.Count;
            _cards.Add(card);
            _index.Add(card, index);
            if (card.Effects != null)
            {
                for (int i = 0; i < card.Effects.Count; i++)
                {
                    var summon = card.Effects[i]?.Action?.Summon;
                    if (summon != null)
                        Add(summon);
                }
            }
            return index;
        }

        public void AddRange(IReadOnlyList<CardDefinition> cards)
        {
            for (int i = 0; i < cards.Count; i++)
                Add(cards[i]);
        }

        /// <summary>번호. 목록에 없으면 -1.</summary>
        public int IndexOf(CardDefinition card) => card != null && _index.TryGetValue(card, out int index) ? index : -1;

        sealed class ReferenceComparer : IEqualityComparer<CardDefinition>
        {
            public static readonly ReferenceComparer Instance = new ReferenceComparer();
            public bool Equals(CardDefinition x, CardDefinition y) => ReferenceEquals(x, y);
            public int GetHashCode(CardDefinition obj) => RuntimeHelpers.GetHashCode(obj);
        }
    }
}
