using System.Collections.Generic;
using TCG.Core.Data;
using TCG.Core.Serialization;
using UnityEngine;

namespace TCG.Client.Battle
{
    /// <summary>Resources/SampleCards, Resources/SampleDecks의 샘플 카드·덱을 읽는다 (Assets/03_Datas/Resources).</summary>
    public static class SampleData
    {
        public static CardCatalog LoadCatalog()
        {
            var catalog = new CardCatalog();
            var assets = Resources.LoadAll<TextAsset>("SampleCards");
            System.Array.Sort(assets, (a, b) => string.CompareOrdinal(a.name, b.name));
            foreach (var asset in assets)
                catalog.Add(CardSerializer.Deserialize(asset.text));
            return catalog;
        }

        public static List<DeckDefinition> LoadDecks()
        {
            var assets = Resources.LoadAll<TextAsset>("SampleDecks");
            System.Array.Sort(assets, (a, b) => string.CompareOrdinal(a.name, b.name));
            var decks = new List<DeckDefinition>();
            foreach (var asset in assets)
                decks.Add(DeckSerializer.Deserialize(asset.text));
            return decks;
        }
    }
}
