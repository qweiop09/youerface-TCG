using System.Collections.Generic;

namespace TCG.Core.Data
{
    /// <summary>카드가 참조하는 에셋 해시 모음 (포함된 소환물까지). 대전 시작 때 없는 에셋을 주고받는 데 쓴다.</summary>
    public static class CardAssetRefs
    {
        /// <summary>처음 나온 순서대로, 중복 없이.</summary>
        public static List<string> Collect(CardDefinition card)
        {
            var result = new List<string>();
            Collect(card, result);
            return result;
        }

        static void Collect(CardDefinition card, List<string> result)
        {
            if (card == null)
                return;
            AddIfAny(card.Art?.Image, result);
            AddIfAny(card.Sounds?.Enter, result);
            AddIfAny(card.Sounds?.Exit, result);
            if (card.Effects == null)
                return;
            for (int i = 0; i < card.Effects.Count; i++)
                Collect(card.Effects[i]?.Action?.Summon, result);
        }

        static void AddIfAny(string hash, List<string> result)
        {
            if (!string.IsNullOrEmpty(hash) && !result.Contains(hash))
                result.Add(hash);
        }
    }
}
