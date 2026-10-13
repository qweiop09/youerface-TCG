using System;
using System.Collections.Generic;
using System.IO;
using TCG.Core.Data;
using TCG.Core.Serialization;
using UnityEngine;

namespace TCG.Client.CardMaker
{
    /// <summary>
    /// 내 카드 보관함 (M4-4): persistentDataPath/Cards/&lt;카드 ID&gt;.json. 형식은 카드 JSON(Architecture §4) 그대로.
    /// 그림·사운드는 해시로만 참조하고 파일은 유저 에셋 저장소에 있다.
    /// </summary>
    public sealed class LocalCardLibrary
    {
        public readonly struct Entry
        {
            public readonly string Id;
            public readonly CardDefinition Card;
            public readonly DateTime Modified;

            public Entry(string id, CardDefinition card, DateTime modified)
            {
                Id = id;
                Card = card;
                Modified = modified;
            }
        }

        readonly string _root;

        public LocalCardLibrary(string root = null)
        {
            _root = root ?? Path.Combine(Application.persistentDataPath, "Cards");
            Directory.CreateDirectory(_root);
        }

        public string Root => _root;

        string PathOf(string id) => Path.Combine(_root, Safe(id) + ".json");

        /// <summary>파일 이름에 쓸 수 없는 글자를 막는다 (ID는 편집기가 c_+16진수로 만든다).</summary>
        static string Safe(string id)
        {
            if (string.IsNullOrEmpty(id))
                throw new ArgumentException("카드 ID가 없습니다.");
            foreach (char c in id)
            {
                if (!(char.IsLetterOrDigit(c) || c == '_' || c == '-'))
                    throw new ArgumentException("카드 ID에 쓸 수 없는 글자가 있습니다: " + id);
            }
            return id;
        }

        public void Save(CardDefinition card)
        {
            string path = PathOf(card.Id);
            string temp = path + ".tmp";
            File.WriteAllText(temp, CardSerializer.Serialize(card));
            if (File.Exists(path))
                File.Delete(path);
            File.Move(temp, path);
        }

        public bool Exists(string id) => File.Exists(PathOf(id));

        public CardDefinition Load(string id)
        {
            string path = PathOf(id);
            return File.Exists(path) ? CardSerializer.Deserialize(File.ReadAllText(path)) : null;
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
                    var card = CardSerializer.Deserialize(File.ReadAllText(path));
                    result.Add(new Entry(Path.GetFileNameWithoutExtension(path), card, File.GetLastWriteTimeUtc(path)));
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"카드 파일을 읽을 수 없습니다: {Path.GetFileName(path)} ({e.Message})");
                }
            }
            result.Sort((a, b) => b.Modified != a.Modified ? b.Modified.CompareTo(a.Modified) : string.CompareOrdinal(a.Id, b.Id));
            return result;
        }
    }
}
