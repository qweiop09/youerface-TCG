using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace TCG.Core.Serialization
{
    /// <summary>Core 데이터 JSON 공통 설정: camelCase 속성명, 열거형은 이름 문자열, null 속성 생략.</summary>
    public static class CoreJson
    {
        public static JsonSerializerSettings CreateSettings(bool indented = false)
        {
            return new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                Converters = { new StringEnumConverter() },
                NullValueHandling = NullValueHandling.Ignore,
                MissingMemberHandling = MissingMemberHandling.Ignore,
                Formatting = indented ? Formatting.Indented : Formatting.None,
            };
        }

        public static JsonSerializer CreateSerializer(bool indented = false)
        {
            return JsonSerializer.Create(CreateSettings(indented));
        }
    }
}
