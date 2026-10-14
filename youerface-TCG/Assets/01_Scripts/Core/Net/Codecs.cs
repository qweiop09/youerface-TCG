using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TCG.Core.Commands;
using TCG.Core.Events;
using TCG.Core.Serialization;

namespace TCG.Core.Net
{
    /// <summary>
    /// 이벤트 ↔ JSON. {"t": 형식 이름(Event 뺀 것), 속성들(camelCase)}. 이벤트는 읽기 전용 속성 + 같은 이름의 생성자 인자 규칙을 따른다.
    /// </summary>
    public static class EventCodec
    {
        static Dictionary<string, Type> s_types;
        static readonly JsonSerializer Serializer = CoreJson.CreateSerializer();

        static Dictionary<string, Type> Types
        {
            get
            {
                if (s_types != null)
                    return s_types;
                var map = new Dictionary<string, Type>();
                foreach (var type in typeof(GameEvent).Assembly.GetTypes())
                {
                    if (!type.IsAbstract && typeof(GameEvent).IsAssignableFrom(type))
                        map[NameOf(type)] = type;
                }
                return s_types = map;
            }
        }

        static string NameOf(Type type)
        {
            string name = type.Name;
            return name.EndsWith("Event", StringComparison.Ordinal) ? name.Substring(0, name.Length - 5) : name;
        }

        public static JObject Encode(GameEvent e)
        {
            var json = JObject.FromObject(e, Serializer);
            json.AddFirst(new JProperty("t", NameOf(e.GetType())));
            return json;
        }

        public static GameEvent Decode(JObject json)
        {
            string name = (string)json["t"];
            if (name == null || !Types.TryGetValue(name, out var type))
                throw new FormatException($"Unknown event: {name}");

            ConstructorInfo best = null;
            foreach (var ctor in type.GetConstructors())
            {
                if (best == null || ctor.GetParameters().Length > best.GetParameters().Length)
                    best = ctor;
            }
            var parameters = best.GetParameters();
            var args = new object[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                var p = parameters[i];
                var token = json.GetValue(p.Name, StringComparison.OrdinalIgnoreCase);
                args[i] = token == null || token.Type == JTokenType.Null
                    ? (p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null)
                    : token.ToObject(p.ParameterType, Serializer);
            }
            return (GameEvent)best.Invoke(args);
        }

        public static JArray EncodeAll(IReadOnlyList<GameEvent> events)
        {
            var array = new JArray();
            for (int i = 0; i < events.Count; i++)
                array.Add(Encode(events[i]));
            return array;
        }

        public static List<GameEvent> DecodeAll(JArray array)
        {
            var result = new List<GameEvent>();
            if (array == null)
                return result;
            for (int i = 0; i < array.Count; i++)
                result.Add(Decode((JObject)array[i]));
            return result;
        }
    }

    /// <summary>명령의 전송 형태. 보낸 사람의 자리는 받는 호스트가 정한다(메시지에 적힌 자리는 믿지 않음).</summary>
    public sealed class CommandData
    {
        public string Kind { get; set; }
        public int? CardId { get; set; }
        public int? TargetId { get; set; }
        public int? Position { get; set; }
        public int? AttackerId { get; set; }
        public int? TriggerId { get; set; }

        public static CommandData From(Command command)
        {
            switch (command)
            {
                case PlayCardCommand play:
                    return new CommandData { Kind = "play", CardId = play.CardId, TargetId = play.TargetId, Position = play.Position };
                case AttackCommand attack:
                    return new CommandData { Kind = "attack", AttackerId = attack.AttackerId, TargetId = attack.TargetId };
                case EndTurnCommand _:
                    return new CommandData { Kind = "endTurn" };
                case ChainActivateCommand activate:
                    return new CommandData { Kind = "chain", TriggerId = activate.TriggerId };
                case PassChainCommand _:
                    return new CommandData { Kind = "pass" };
                case ConcedeCommand _:
                    return new CommandData { Kind = "concede" };
                default:
                    throw new ArgumentException("Unknown command: " + command?.GetType().Name);
            }
        }

        /// <summary>자리를 붙여 명령으로. 형식이 틀리면 null.</summary>
        public Command ToCommand(int seat)
        {
            switch (Kind)
            {
                case "play": return CardId.HasValue ? new PlayCardCommand(seat, CardId.Value, TargetId, Position) : null;
                case "attack": return AttackerId.HasValue && TargetId.HasValue ? new AttackCommand(seat, AttackerId.Value, TargetId.Value) : null;
                case "endTurn": return new EndTurnCommand(seat);
                case "chain": return TriggerId.HasValue ? new ChainActivateCommand(seat, TriggerId.Value) : null;
                case "pass": return new PassChainCommand(seat);
                case "concede": return new ConcedeCommand(seat);
                default: return null;
            }
        }
    }
}
