using System;
using System.Collections.Generic;
using TCG.Core.Commands;
using TCG.Core.Data;
using TCG.Core.Scoring;
using TCG.Core.Serialization;
using TCG.Core.State;

namespace TCG.Core.Net
{
    public sealed class GuestSetup
    {
        public string AppVersion { get; set; } = "";
        public string Name { get; set; } = "";
        public DeckDefinition Deck { get; set; }
        public CardCatalog Cards { get; set; }
        public ScoreTable Scores { get; set; } = ScoreTable.CreateDefault();
        public AssetLimits Limits { get; set; }
    }

    public enum GuestPhase
    {
        /// <summary>인사 보냄, 방 정보를 기다리는 중.</summary>
        Connecting,
        /// <summary>덱 보냄, 방장 검사 대기.</summary>
        DeckSent,
        /// <summary>방 규칙에 걸림. <see cref="OnlineGuest.SubmitDeck"/>로 다른 덱을 낼 수 있다.</summary>
        DeckRejected,
        Assets,
        Playing,
        /// <summary>방장과 끊김, 재접속 대기.</summary>
        Reconnecting,
        Ended,
        /// <summary>방장이 접속을 거절함 (<see cref="OnlineGuest.RejectReason"/>).</summary>
        Rejected,
    }

    /// <summary>
    /// 게스트 쪽 온라인 흐름: 인사 → 방 규칙·규칙값 받기 → 덱 보내기 → 에셋 교환 → 대전.
    /// 대전 중에는 명령만 보내고, 받은 시야 상태로 <see cref="SeatUpdate"/>를 만들어 <see cref="Updates"/>에 쌓는다.
    /// 방장과 끊기면 <see cref="RuleConfig.HostReconnectWaitSeconds"/> 동안 <see cref="Rejoin"/>을 기다린다.
    /// </summary>
    public sealed class OnlineGuest
    {
        readonly GuestSetup _setup;
        readonly IAssetSource _assetStore;
        readonly List<CardDefinition> _catalog = new List<CardDefinition>();
        ITransport _transport;
        AssetExchange _assets;
        bool _helloSent;
        bool _readySent;
        int _nextSeq = 1;
        int _lostMs;

        public GuestPhase Phase { get; private set; } = GuestPhase.Connecting;
        public string HostName { get; private set; }
        public RoomRules Room { get; private set; }
        public RuleConfig Rules { get; private set; }
        public DeckDefinition HostDeck { get; private set; }
        public IReadOnlyList<CardDefinition> HostCards { get; private set; }
        public List<string> DeckIssues { get; } = new List<string>();
        public string RejectReason { get; private set; }
        public int Seat { get; private set; } = OnlineHost.GuestSeat;
        public string RejoinToken { get; private set; }
        /// <summary>지금 시야 상태 (대전 전이면 null).</summary>
        public GameState View { get; private set; }
        public Queue<SeatUpdate> Updates { get; } = new Queue<SeatUpdate>();
        /// <summary>시간 상태. 받은 뒤로 흐른 시간만큼 줄여 둔다.</summary>
        public MatchClock Clock { get; private set; }
        public MatchEndReason EndReason { get; private set; }
        /// <summary>마지막으로 거절된 명령의 이유 (읽으면 지움).</summary>
        public string LastCommandRejection { get; set; }
        public int ReconnectRemainingMs => Phase == GuestPhase.Reconnecting && Rules != null
            ? Math.Max(0, Rules.HostReconnectWaitSeconds * 1000 - _lostMs) : 0;
        public AssetExchange Assets => _assets;

        public OnlineGuest(GuestSetup setup, ITransport transport, IAssetSource assetStore)
        {
            _setup = setup ?? throw new ArgumentNullException(nameof(setup));
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _assetStore = assetStore;
        }

        public void Update(int elapsedMs)
        {
            if (!_helloSent && _transport.State == LinkState.Connected)
            {
                _helloSent = true;
                Send(new HelloMsg
                {
                    Protocol = NetProtocol.Version,
                    App = _setup.AppVersion,
                    CardSchema = CardSerializer.CurrentSchemaVersion,
                    ScoreTable = _setup.Scores.Version,
                    Name = _setup.Name,
                    RejoinToken = Phase == GuestPhase.Reconnecting ? RejoinToken : null,
                });
            }

            while (_transport.TryReceive(out var text))
            {
                var message = NetCodec.Decode(text);
                if (message != null)
                    Handle(message);
            }

            if (Phase == GuestPhase.Assets && !_readySent && _assets.Done)
            {
                _readySent = true;
                Send(new AssetsReadyMsg());
            }

            if (Phase == GuestPhase.Playing && Clock != null)
            {
                Clock.RemainingMs = Math.Max(0, Clock.RemainingMs - elapsedMs);
                Clock.DisconnectRemainingMs = Math.Max(0, Clock.DisconnectRemainingMs - elapsedMs);
            }

            if (_transport.State == LinkState.Closed)
                LinkLost(elapsedMs);
        }

        void LinkLost(int elapsedMs)
        {
            switch (Phase)
            {
                case GuestPhase.Playing:
                    Phase = GuestPhase.Reconnecting;
                    _lostMs = 0;
                    break;
                case GuestPhase.Reconnecting:
                    _lostMs += elapsedMs;
                    if (Rules != null && _lostMs >= Rules.HostReconnectWaitSeconds * 1000)
                    {
                        Phase = GuestPhase.Ended;
                        EndReason = MatchEndReason.HostLost;
                    }
                    break;
                case GuestPhase.Ended:
                case GuestPhase.Rejected:
                    break;
                default:
                    Phase = GuestPhase.Rejected;
                    RejectReason = RejectReason ?? "연결이 끊겼습니다.";
                    break;
            }
        }

        /// <summary>새 연결로 다시 접속 (대전 중 끊겼을 때). 받은 표로 같은 자리에 돌아간다.</summary>
        public void Rejoin(ITransport transport)
        {
            if (Phase != GuestPhase.Reconnecting)
                return;
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _helloSent = false;
        }

        /// <summary>덱을 (다시) 낸다. 방 규칙에 걸린 뒤 다른 덱을 고를 때.</summary>
        public void SubmitDeck(DeckDefinition deck, CardCatalog cards)
        {
            if (Phase != GuestPhase.DeckRejected && Phase != GuestPhase.Connecting)
                return;
            _setup.Deck = deck;
            _setup.Cards = cards;
            SendDeck();
        }

        void SendDeck()
        {
            var distinct = new List<CardDefinition>();
            var resolved = _setup.Cards.Resolve(_setup.Deck);
            for (int i = 0; i < resolved.Count; i++)
            {
                if (!distinct.Contains(resolved[i]))
                    distinct.Add(resolved[i]);
            }
            DeckIssues.Clear();
            Phase = GuestPhase.DeckSent;
            Send(new DeckSubmitMsg { Deck = _setup.Deck, Cards = NetCodec.EncodeCards(distinct) });
        }

        /// <summary>명령을 보낸다. 결과는 다음 변화(Updates) 또는 <see cref="LastCommandRejection"/>.</summary>
        public int Submit(Command command)
        {
            if (Phase != GuestPhase.Playing)
                return 0;
            int seq = _nextSeq++;
            Send(new CommandMsg { Seq = seq, Command = CommandData.From(command) });
            return seq;
        }

        public void Close()
        {
            _transport.Close();
            if (Phase != GuestPhase.Ended)
                Phase = GuestPhase.Ended;
        }

        void Send(NetMessage message) => _transport.Send(NetCodec.Encode(message));

        void Handle(NetMessage message)
        {
            if (_assets != null && _assets.Handle(message, _transport))
                return;

            switch (message)
            {
                case RejectMsg reject:
                    RejectReason = reject.Reason;
                    Phase = Phase == GuestPhase.Reconnecting ? GuestPhase.Ended : GuestPhase.Rejected;
                    if (Phase == GuestPhase.Ended)
                        EndReason = MatchEndReason.HostLost;
                    break;
                case WelcomeMsg welcome:
                    HostName = welcome.HostName;
                    Room = welcome.Room ?? new RoomRules();
                    Rules = welcome.Rules ?? RuleConfig.CreateDefault();
                    SendDeck();
                    break;
                case DeckRejectedMsg rejected:
                    Phase = GuestPhase.DeckRejected;
                    DeckIssues.Clear();
                    if (rejected.Issues != null)
                        DeckIssues.AddRange(rejected.Issues);
                    break;
                case DeckAcceptedMsg accepted:
                    HostDeck = accepted.Deck;
                    var hostCards = NetCodec.DecodeCards(accepted.Cards);
                    HostCards = hostCards;
                    Phase = GuestPhase.Assets;
                    _assets = new AssetExchange(_assetStore, _setup.Limits);
                    _assets.RequestMissing(hostCards, _transport);
                    break;
                case MatchStartMsg start:
                    Seat = start.Seat;
                    RejoinToken = start.RejoinToken;
                    Phase = GuestPhase.Playing;
                    break;
                case UpdateMsg update:
                    HandleUpdate(update);
                    break;
                case ClockMsg clock:
                    Clock = clock.Clock;
                    break;
                case CommandRejectedMsg rejected:
                    LastCommandRejection = rejected.Reason;
                    break;
                case ResyncMsg resync:
                    HandleResync(resync);
                    break;
            }
        }

        void HandleUpdate(UpdateMsg update)
        {
            _catalog.AddRange(NetCodec.DecodeCards(update.CatalogAdds));
            var after = update.Snapshot.ToState(Rules, _catalog);
            var before = update.Before != null
                ? PlayerView.Reveal(update.Before.ToState(Rules, _catalog), after)
                : PlayerView.Reveal(View ?? after, after);
            View = after;
            Clock = update.Clock;
            EndReason = update.End;
            if (after.Phase == GamePhase.Finished)
                Phase = GuestPhase.Ended;
            Updates.Enqueue(new SeatUpdate
            {
                Seat = Seat,
                Events = EventCodec.DecodeAll(update.Events),
                Before = before,
                After = after,
                Auto = update.Auto,
                AutoPlayer = update.AutoPlayer,
                End = update.End,
                Clock = update.Clock,
            });
        }

        void HandleResync(ResyncMsg resync)
        {
            Seat = resync.Seat;
            Rules = resync.Rules ?? Rules;
            _catalog.Clear();
            _catalog.AddRange(NetCodec.DecodeCards(resync.Catalog));
            View = resync.Snapshot.ToState(Rules, _catalog);
            Clock = resync.Clock;
            EndReason = resync.End;
            Phase = View.Phase == GamePhase.Finished ? GuestPhase.Ended : GuestPhase.Playing;
            _lostMs = 0;
            Updates.Enqueue(new SeatUpdate
            {
                Seat = Seat,
                Events = new List<Events.GameEvent>(),
                Before = View,
                After = View,
                End = resync.End,
                Clock = resync.Clock,
                Resync = true,
            });
        }
    }
}
