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
        public const int CurrentSchemaVersion = 3;
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
            MigrateV1ToV2,
            MigrateV2ToV3,
        };

        /// <summary>v1 → v2: 문자열 presentationStyle을 등장·퇴장 연출 설정 객체로 바꾼다 (효과 연출 설정은 기본값).</summary>
        static JObject MigrateV1ToV2(JObject root)
        {
            MigrateCardV1ToV2(root);
            return root;
        }

        static void MigrateCardV1ToV2(JObject card)
        {
            var style = card["presentationStyle"];
            if (style != null)
            {
                string value = style.Type == JTokenType.String ? style.Value<string>() : "default";
                card.Remove("presentationStyle");
                card["presentation"] = new JObject { ["enter"] = value, ["exit"] = value };
            }

            // 소환물 정의는 효과 안에 들어 있으므로 함께 변환한다.
            if (card["effects"] is JArray effects)
            {
                foreach (var effect in effects)
                {
                    if (effect?["action"]?["summon"] is JObject summon)
                        MigrateCardV1ToV2(summon);
                }
            }
        }

        /// <summary>
        /// v2 → v3: 피해 유형 분리 (GameDesign §7.6). 트리거 "OnDamaged"(피격 시) → "OnAnyDamageTaken"(모든 피해를 받을 때).
        /// 카드 설명(flavor)은 새 선택 칸이라 변환할 것이 없다.
        /// </summary>
        static JObject MigrateV2ToV3(JObject root)
        {
            MigrateCardV2ToV3(root);
            return root;
        }

        static void MigrateCardV2ToV3(JObject card)
        {
            if (!(card["effects"] is JArray effects))
                return;
            foreach (var effect in effects)
            {
                if (effect?["trigger"] is JObject trigger && trigger["type"] is JValue type &&
                    type.Type == JTokenType.String && (string)type == "OnDamaged")
                    trigger["type"] = "OnAnyDamageTaken";
                if (effect?["action"]?["summon"] is JObject summon)
                    MigrateCardV2ToV3(summon);
            }
        }

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
