using Newtonsoft.Json.Linq;
using NUnit.Framework;
using TCG.Core.Data;
using TCG.Core.Serialization;

namespace TCG.Tests.Core
{
    public class CardSerializerTests
    {
        [Test]
        public void RoundTrip_Minion_PreservesAllFields()
        {
            var original = TestCards.FireSpirit();

            string json = CardSerializer.Serialize(original);
            var restored = CardSerializer.Deserialize(json);

            Assert.That(restored.Id, Is.EqualTo("c_7f3a"));
            Assert.That(restored.Name, Is.EqualTo("불꽃 정령"));
            Assert.That(restored.Type, Is.EqualTo(CardType.Minion));
            Assert.That(restored.Cost, Is.EqualTo(3));
            Assert.That(restored.Attack, Is.EqualTo(3));
            Assert.That(restored.Health, Is.EqualTo(2));
            Assert.That(restored.Usage, Is.Null);
            Assert.That(restored.Keywords, Is.EqualTo(new[] { Keyword.Taunt }));
            Assert.That(restored.Art.Image, Is.EqualTo("sha256:ab12"));
            Assert.That(restored.Sounds.Enter, Is.EqualTo("sha256:cd34"));
            Assert.That(restored.Sounds.Exit, Is.EqualTo("sha256:ef56"));
            Assert.That(restored.PresentationStyle, Is.EqualTo("default"));
            Assert.That(restored.Effects, Has.Count.EqualTo(1));
            Assert.That(restored.Effects[0].Trigger.Type, Is.EqualTo(TriggerType.OnEnter));
            Assert.That(restored.Effects[0].Target.Type, Is.EqualTo(TargetType.RandomEnemyMinion));
            Assert.That(restored.Effects[0].Target.Count, Is.EqualTo(1));
            Assert.That(restored.Effects[0].Action.Type, Is.EqualTo(ActionType.Damage));
            Assert.That(restored.Effects[0].Action.Amount, Is.EqualTo(2));
        }

        [Test]
        public void RoundTrip_ReserializesToIdenticalJson()
        {
            string first = CardSerializer.Serialize(TestCards.SummonSpell());
            string second = CardSerializer.Serialize(CardSerializer.Deserialize(first));

            Assert.That(JToken.DeepEquals(JObject.Parse(first), JObject.Parse(second)), Is.True);
        }

        [Test]
        public void RoundTrip_NestedSummon_IsPreserved()
        {
            var restored = CardSerializer.Deserialize(CardSerializer.Serialize(TestCards.SummonSpell()));

            var effect = restored.Effects[0];
            Assert.That(effect.Conditions, Has.Count.EqualTo(1));
            Assert.That(effect.Conditions[0].CardType, Is.EqualTo(CardType.Field));

            var summon = effect.Action.Summon;
            Assert.That(summon, Is.Not.Null);
            Assert.That(summon.Type, Is.EqualTo(CardType.Summon));
            Assert.That(summon.Usage, Is.EqualTo(2));
            Assert.That(summon.OnDuplicate, Is.EqualTo(SummonDuplicateMode.AddNew));
            Assert.That(summon.Effects[0].Trigger.Type, Is.EqualTo(TriggerType.OnTurnEnd));
        }

        [Test]
        public void Serialize_WritesSchemaVersionFirst_AndOnlyAtTopLevel()
        {
            var json = JObject.Parse(CardSerializer.Serialize(TestCards.SummonSpell()));

            var first = (JProperty)json.First;
            Assert.That(first.Name, Is.EqualTo("schemaVersion"));
            Assert.That(first.Value.Value<int>(), Is.EqualTo(CardSerializer.CurrentSchemaVersion));
            Assert.That(json["effects"][0]["action"]["summon"]["schemaVersion"], Is.Null);
        }

        [Test]
        public void Serialize_UsesCamelCaseAndEnumNames_AndOmitsNulls()
        {
            var json = JObject.Parse(CardSerializer.Serialize(TestCards.Field()));

            Assert.That(json["type"].Value<string>(), Is.EqualTo("Field"));
            Assert.That(json["presentationStyle"].Value<string>(), Is.EqualTo("default"));
            Assert.That(json["attack"], Is.Null);
            Assert.That(json["health"], Is.Null);
            Assert.That(json["usage"], Is.Null);
        }

        [Test]
        public void Deserialize_ArchitectureSampleJson()
        {
            const string json = @"{
                ""schemaVersion"": 1,
                ""id"": ""c_7f3a"",
                ""name"": ""불꽃 정령"",
                ""type"": ""Minion"",
                ""cost"": 3, ""attack"": 3, ""health"": 2,
                ""keywords"": [""Taunt""],
                ""art"": { ""image"": ""sha256:ab12"" },
                ""sounds"": { ""enter"": ""sha256:cd34"", ""exit"": ""sha256:ef56"" },
                ""presentationStyle"": ""default"",
                ""effects"": [ {
                    ""trigger"": { ""type"": ""OnEnter"" },
                    ""conditions"": [],
                    ""target"": { ""type"": ""RandomEnemyMinion"", ""count"": 1 },
                    ""action"": { ""type"": ""Damage"", ""amount"": 2 }
                } ]
            }";

            var card = CardSerializer.Deserialize(json);

            Assert.That(card.Name, Is.EqualTo("불꽃 정령"));
            Assert.That(card.Keywords, Is.EqualTo(new[] { Keyword.Taunt }));
            Assert.That(card.Effects[0].Action.Amount, Is.EqualTo(2));
            Assert.That(CardValidator.Validate(card), Is.Empty);
        }

        [Test]
        public void Deserialize_MissingSchemaVersion_Throws()
        {
            Assert.Throws<CardSerializationException>(() =>
                CardSerializer.Deserialize(@"{ ""name"": ""x"", ""type"": ""Spell"" }"));
        }

        [Test]
        public void Deserialize_NewerSchemaVersion_Throws()
        {
            string json = $@"{{ ""schemaVersion"": {CardSerializer.CurrentSchemaVersion + 1}, ""type"": ""Spell"" }}";
            Assert.Throws<CardSerializationException>(() => CardSerializer.Deserialize(json));
        }

        [Test]
        public void Deserialize_MalformedJson_Throws()
        {
            Assert.Throws<CardSerializationException>(() => CardSerializer.Deserialize("{ not json"));
        }

        [Test]
        public void Deserialize_UnknownEnumValue_Throws()
        {
            Assert.Throws<CardSerializationException>(() =>
                CardSerializer.Deserialize(@"{ ""schemaVersion"": 1, ""type"": ""Hero"" }"));
        }
    }
}
