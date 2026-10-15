using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TCG.Core.Data;
using TCG.Core.Serialization;

namespace TCG.Core.Net
{
    /// <summary>
    /// 온라인 메시지 (Architecture §6). JSON 한 덩어리 = 메시지 1개, "t"에 종류(클래스 이름에서 Msg 뺀 것).
    /// 핸드셰이크: Hello → Welcome(코인 약속)/Reject → DeckSubmit(게스트 코인) → DeckAccepted/DeckRejected → AssetRequest·AssetChunk·AssetMissing →
    /// AssetsReady → MatchStart(방장 코인 공개) → Update… (게스트는 CommandMsg). 재접속: Hello(rejoinToken) → Resync.
    /// </summary>
    public abstract class NetMessage { }

    public static class NetProtocol
    {
        /// <summary>메시지 형식 버전. 바꾸면 올린다(맞지 않으면 접속 거절).</summary>
        public const int Version = 5; // 2: 피해 유형 (M5.5-0), 3: 기물 칸 번호, 4: 카드 사용 이벤트의 고른 대상, 5: 코인 토스(양쪽 비밀값)
        /// <summary>에셋 조각 크기(원본 바이트). Relay 패킷 제한은 전송 구현이 따로 처리한다.</summary>
        public const int AssetChunkBytes = 32 * 1024;
    }

    public sealed class HelloMsg : NetMessage
    {
        public int Protocol { get; set; }
        public string App { get; set; }
        public int CardSchema { get; set; }
        public int ScoreTable { get; set; }
        public string Name { get; set; }
        /// <summary>재접속이면 MatchStart에서 받은 표. 처음이면 null.</summary>
        public string RejoinToken { get; set; }
    }

    public sealed class RejectMsg : NetMessage
    {
        public string Reason { get; set; }
    }

    public sealed class WelcomeMsg : NetMessage
    {
        public string HostName { get; set; }
        public RoomRules Room { get; set; }
        public RuleConfig Rules { get; set; }
        /// <summary>방장 코인 비밀값의 해시 (<see cref="CoinToss.Commit"/>). 비밀값은 MatchStart에서 공개.</summary>
        public string CoinCommit { get; set; }
    }

    public sealed class DeckSubmitMsg : NetMessage
    {
        /// <summary>게스트 코인 비밀값 (<see cref="CoinToss"/>).</summary>
        public string CoinSecret { get; set; }
        public DeckDefinition Deck { get; set; }
        /// <summary>덱에 든 카드 정의(<see cref="CardSerializer"/> JSON), 한 번씩.</summary>
        public List<string> Cards { get; set; } = new List<string>();
    }

    public sealed class DeckRejectedMsg : NetMessage
    {
        public List<string> Issues { get; set; } = new List<string>();
    }

    /// <summary>게스트 덱 통과 + 방장 덱 공개 (에셋 교환용).</summary>
    public sealed class DeckAcceptedMsg : NetMessage
    {
        public DeckDefinition Deck { get; set; }
        public List<string> Cards { get; set; } = new List<string>();
    }

    public sealed class AssetRequestMsg : NetMessage
    {
        public List<string> Hashes { get; set; } = new List<string>();
    }

    public sealed class AssetChunkMsg : NetMessage
    {
        public string Hash { get; set; }
        public string Extension { get; set; }
        public int Index { get; set; }
        public int Count { get; set; }
        public int TotalBytes { get; set; }
        /// <summary>Base64.</summary>
        public string Data { get; set; }
    }

    public sealed class AssetMissingMsg : NetMessage
    {
        public string Hash { get; set; }
    }

    /// <summary>게스트: 받을 에셋을 다 받았다(없는 것은 기본 표시).</summary>
    public sealed class AssetsReadyMsg : NetMessage { }

    public sealed class MatchStartMsg : NetMessage
    {
        public int Seat { get; set; }
        public string RejoinToken { get; set; }
        /// <summary>방장 코인 비밀값 공개. 게스트는 Welcome의 해시와 맞는지 확인한다.</summary>
        public string CoinSecret { get; set; }
    }

    public sealed class UpdateMsg : NetMessage
    {
        public JArray Events { get; set; }
        public StateSnapshot Snapshot { get; set; }
        /// <summary>대전 첫 변화에만: 이전 상태.</summary>
        public StateSnapshot Before { get; set; }
        /// <summary>이번에 새로 쓰인 카드 정의(<see cref="MatchCatalog"/> 뒤에 붙는 순서).</summary>
        public List<string> CatalogAdds { get; set; }
        public AutoReason Auto { get; set; }
        public int AutoPlayer { get; set; } = -1;
        public MatchEndReason End { get; set; }
        public MatchClock Clock { get; set; }
    }

    public sealed class ClockMsg : NetMessage
    {
        public MatchClock Clock { get; set; }
    }

    public sealed class CommandMsg : NetMessage
    {
        public int Seq { get; set; }
        public CommandData Command { get; set; }
    }

    public sealed class CommandRejectedMsg : NetMessage
    {
        public int Seq { get; set; }
        public string Reason { get; set; }
    }

    /// <summary>재접속: 지금 시야 상태 전체와 카드 정의 전체.</summary>
    public sealed class ResyncMsg : NetMessage
    {
        public int Seat { get; set; }
        public RuleConfig Rules { get; set; }
        public List<string> Catalog { get; set; } = new List<string>();
        public StateSnapshot Snapshot { get; set; }
        public MatchEndReason End { get; set; }
        public MatchClock Clock { get; set; }
    }

    public static class NetCodec
    {
        static readonly JsonSerializer Serializer = CoreJson.CreateSerializer();
        static readonly Dictionary<string, Type> Types = new Dictionary<string, Type>();

        static NetCodec()
        {
            foreach (var type in typeof(NetMessage).Assembly.GetTypes())
            {
                if (!type.IsAbstract && typeof(NetMessage).IsAssignableFrom(type))
                    Types[NameOf(type)] = type;
            }
        }

        static string NameOf(Type type)
        {
            string name = type.Name;
            return name.EndsWith("Msg", StringComparison.Ordinal) ? name.Substring(0, name.Length - 3) : name;
        }

        public static string Encode(NetMessage message)
        {
            var json = JObject.FromObject(message, Serializer);
            json.AddFirst(new JProperty("t", NameOf(message.GetType())));
            return json.ToString(Formatting.None);
        }

        /// <summary>모르는 종류·깨진 JSON이면 null.</summary>
        public static NetMessage Decode(string text)
        {
            try
            {
                var json = JObject.Parse(text);
                string name = (string)json["t"];
                if (name == null || !Types.TryGetValue(name, out var type))
                    return null;
                json.Remove("t");
                return (NetMessage)json.ToObject(type, Serializer);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        public static List<string> EncodeCards(IReadOnlyList<CardDefinition> cards, int start = 0)
        {
            var list = new List<string>();
            for (int i = start; i < cards.Count; i++)
                list.Add(CardSerializer.Serialize(cards[i], indented: false));
            return list;
        }

        public static List<CardDefinition> DecodeCards(List<string> cards)
        {
            var list = new List<CardDefinition>();
            if (cards == null)
                return list;
            for (int i = 0; i < cards.Count; i++)
                list.Add(CardSerializer.Deserialize(cards[i]));
            return list;
        }
    }
}
