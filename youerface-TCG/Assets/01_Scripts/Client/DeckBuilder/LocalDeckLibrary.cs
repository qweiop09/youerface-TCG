using System;
using System.Collections.Generic;
using System.IO;
using TCG.Client.CardMaker;
using TCG.Core.Data;
using TCG.Core.Serialization;
using UnityEngine;

namespace TCG.Client.DeckBuilder
{
    /// <summary>
    /// 내 덱 보관함 (M5-3): persistentDataPath/Decks/&lt;덱 ID&gt;.json. 형식은 덱 JSON v1(Architecture §4) 그대로.
    /// 카드는 ID로만 참조한다(카드 파일은 <see cref="LocalCardLibrary"/>, 샘플은 Resources).
    /// </summary>
    public sealed class LocalDeckLibrary
    {
        public readonly struct Entry
        {
            public readonly string Id;
            public readonly DeckDefinition Deck;
            public readonly DateTime Modified;

            public Entry(string id, DeckDefinition deck, DateTime modified)
            {
                Id = id;
                Deck = deck;
                Modified = modified;
            }
        }

        readonly string _root;

        public LocalDeckLibrary(string root = null)
        {
            _root = root ?? Path.Combine(Application.persistentDataPath, "Decks");
            Directory.CreateDirectory(_root);
        }

        public string Root => _root;

        string PathOf(string id) => Path.Combine(_root, LocalCardLibrary.Safe(id) + ".json");

        public void Save(DeckDefinition deck) => LocalCardLibrary.WriteAtomic(PathOf(deck.Id), DeckSerializer.Serialize(deck));

        public bool Exists(string id) => File.Exists(PathOf(id));

        public DeckDefinition Load(string id)
        {
            string path = PathOf(id);
            return File.Exists(path) ? DeckSerializer.Deserialize(File.ReadAllText(path)) : null;
        }

        public void Delete(string id)
        {
            string path = PathOf(id);
            if (File.Exists(path))
                File.Delete(path);
        }

        /// <summary>최근 수정 순. 읽지 못한 파일은 건너뛰고 로그만 남긴다.</summary>
        public List<Entry> List()
        {
            var result = new List<Entry>();
            foreach (var path in Directory.GetFiles(_root, "*.json"))
            {
                try
                {
                    var deck = DeckSerializer.Deserialize(File.ReadAllText(path));
                    result.Add(new Entry(Path.GetFileNameWithoutExtension(path), deck, File.GetLastWriteTimeUtc(path)));
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"덱 파일을 읽을 수 없습니다: {Path.GetFileName(path)} ({e.Message})");
                }
            }
            result.Sort((a, b) => b.Modified != a.Modified ? b.Modified.CompareTo(a.Modified) : string.CompareOrdinal(a.Id, b.Id));
            return result;
        }
    }
}
