using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TCG.Core.Data;

namespace TCG.Core.Serialization
{
    /// <summary>덱 JSON 변환. 최상위에 schemaVersion을 붙인다 (덱 형식 버전, 카드 형식과 별개).</summary>
    public static class DeckSerializer
    {
        public const int CurrentSchemaVersion = 1;

        public static string Serialize(DeckDefinition deck, bool indented = true)
        {
            if (deck == null)
                throw new ArgumentNullException(nameof(deck));
            var json = JObject.FromObject(deck, CoreJson.CreateSerializer());
            json.AddFirst(new JProperty(CardSerializer.SchemaVersionProperty, CurrentSchemaVersion));
            return json.ToString(indented ? Formatting.Indented : Formatting.None);
        }

        public static DeckDefinition Deserialize(string json)
        {
            if (json == null)
                throw new ArgumentNullException(nameof(json));
            try
            {
                var root = JObject.Parse(json);
                var token = root[CardSerializer.SchemaVersionProperty];
                if (token == null || token.Type != JTokenType.Integer)
                    throw new CardSerializationException("덱의 schemaVersion이 없거나 정수가 아닙니다.");
                int version = token.Value<int>();
                if (version < 1 || version > CurrentSchemaVersion)
                    throw new CardSerializationException($"지원하지 않는 덱 형식 버전: {version}");
                root.Remove(CardSerializer.SchemaVersionProperty);
                return root.ToObject<DeckDefinition>(CoreJson.CreateSerializer())
                    ?? throw new CardSerializationException("덱 정의가 비어 있습니다.");
            }
            catch (JsonException e)
            {
                throw new CardSerializationException("덱 JSON 형식이 올바르지 않습니다: " + e.Message, e);
            }
        }
    }
}
