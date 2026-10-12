using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TCG.Core.Data;

namespace TCG.Core.Serialization
{
    /// <summary>
    /// 카드 정의 JSON 변환. 최상위 객체에 schemaVersion을 붙이며, 읽을 때 옛 버전이면 마이그레이션한다.
    /// 스키마를 바꾸면 <see cref="CurrentSchemaVersion"/>을 올리고 <see cref="Migrate"/>에 단계를 추가한다.
    /// </summary>
    public static class CardSerializer
    {
        public const int CurrentSchemaVersion = 1;
        public const string SchemaVersionProperty = "schemaVersion";

        public static string Serialize(CardDefinition card, bool indented = true)
        {
            if (card == null)
                throw new ArgumentNullException(nameof(card));

            var json = JObject.FromObject(card, CoreJson.CreateSerializer());
            json.AddFirst(new JProperty(SchemaVersionProperty, CurrentSchemaVersion));
            return json.ToString(indented ? Formatting.Indented : Formatting.None);
        }

        public static CardDefinition Deserialize(string json)
        {
            if (json == null)
                throw new ArgumentNullException(nameof(json));

            try
            {
                var root = JObject.Parse(json);
                int version = ReadSchemaVersion(root);
                root = Migrate(root, version);
                root.Remove(SchemaVersionProperty);

                var card = root.ToObject<CardDefinition>(CoreJson.CreateSerializer());
                if (card == null)
                    throw new CardSerializationException("카드 정의가 비어 있습니다.");
                return card;
            }
            catch (JsonException e)
            {
                throw new CardSerializationException("카드 JSON 형식이 올바르지 않습니다: " + e.Message, e);
            }
        }

        static int ReadSchemaVersion(JObject root)
        {
            var token = root[SchemaVersionProperty];
            if (token == null || token.Type != JTokenType.Integer)
                throw new CardSerializationException($"'{SchemaVersionProperty}'가 없거나 정수가 아닙니다.");

            int version = token.Value<int>();
            if (version < 1)
                throw new CardSerializationException($"잘못된 스키마 버전: {version}");
            if (version > CurrentSchemaVersion)
                throw new CardSerializationException(
                    $"더 새로운 버전({version})에서 만든 카드입니다. 지원 버전: {CurrentSchemaVersion}");
            return version;
        }

        /// <summary>
        /// 버전별 변환 단계. Migrations[i]는 스키마 (i+1) → (i+2) 변환이다.
        /// 스키마를 올릴 때 여기에 단계를 추가한다.
        /// </summary>
        static readonly Func<JObject, JObject>[] Migrations =
        {
        };

        /// <summary>fromVersion → CurrentSchemaVersion으로 한 단계씩 변환한다.</summary>
        static JObject Migrate(JObject root, int fromVersion)
        {
            for (int v = fromVersion; v < CurrentSchemaVersion; v++)
            {
                int step = v - 1;
                if (step >= Migrations.Length)
                    throw new CardSerializationException($"스키마 {v} → {v + 1} 마이그레이션이 없습니다.");
                root = Migrations[step](root);
            }
            return root;
        }
    }

    public sealed class CardSerializationException : Exception
    {
        public CardSerializationException(string message) : base(message) { }
        public CardSerializationException(string message, Exception inner) : base(message, inner) { }
    }
}
