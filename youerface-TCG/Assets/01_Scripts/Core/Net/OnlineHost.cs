using System;
using System.Collections.Generic;
using TCG.Core.Commands;
using TCG.Core.Data;
using TCG.Core.Effects;
using TCG.Core.Rules;
using TCG.Core.Scoring;
using TCG.Core.Serialization;
using TCG.Core.State;

namespace TCG.Core.Net
{
    /// <summary>방장 설정. 덱은 방 규칙을 통과한 것이어야 한다.</summary>
    public sealed class HostSetup
    {
        public string AppVersion { get; set; } = "";
        public string HostName { get; set; } = "";
        public RoomRules Room { get; set; } = new RoomRules();
        public RuleConfig Rules { get; set; } = RuleConfig.CreateDefault();
        public ScoreTable Scores { get; set; } = ScoreTable.CreateDefault();
        public EffectRegistry Registry { get; set; }
        public AssetLimits Limits { get; set; }
        public DeckDefinition Deck { get; set; }
        /// <summary>방장 덱을 해석할 카드 모음.</summary>
        public CardCatalog Cards { get; set; }
        public int Seed { get; set; }
        /// <summary>코인 토스 비밀값. null이면 게스트마다 새로 만든다 (테스트에서만 지정).</summary>
        public string CoinSecret { get; set; }
    }

    public enum HostPhase
    {
        /// <summary>게스트를 기다리는 중.</summary>
        Waiting,
        /// <summary>버전 확인 끝, 게스트 덱을 기다리는 중.</summary>
        Handshake,
        /// <summary>덱 통과, 에셋 교환 중.</summary>
        Assets,
        Playing,
        Ended,
    }

    /// <summary>
    /// 방장 쪽 온라인 흐름 (Architecture §6): 게스트 받기 → 버전 확인 → 덱 검증(방 규칙) → 에셋 교환 → 대전(<see cref="HostMatch"/>).
    /// 방장은 자리 0, 게스트는 자리 1. 방장 화면은 <see cref="LocalUpdates"/>를, 게스트는 메시지를 받는다.
    /// 대전 중 게스트가 끊기면 <see cref="HostMatch.SetConnected"/>로 알리고, 재접속(Hello + 표)이면 Resync를 보낸다.
    /// </summary>
    public sealed class OnlineHost
    {
        public const int HostSeat = 0;
        public const int GuestSeat = 1;

        readonly HostSetup _setup;
        readonly ITransportListener _listener;
        readonly IAssetSource _assetStore;
        readonly List<CardDefinition> _hostCards;
        ITransport _guest;
        bool _guestHello;
        bool _guestReady;
        AssetExchange _assets;
        int _catalogSent;
        bool _sentFirstUpdate;
        string _rejoinToken;
        string _coinSecret;
        string _guestCoin;

        public HostPhase Phase { get; private set; } = HostPhase.Waiting;
        public string GuestName { get; private set; }
        public DeckDefinition GuestDeck { get; private set; }
        public IReadOnlyList<CardDefinition> GuestCards { get; private set; }
        public HostMatch Match { get; private set; }
        /// <summary>코인 토스로 정한 선공 자리 (대전 전이면 -1).</summary>
        public int FirstSeat { get; private set; } = -1;
        /// <summary>방장 화면에 보여 줄 변화 (자리 0).</summary>
        public Queue<SeatUpdate> LocalUpdates { get; } = new Queue<SeatUpdate>();
        /// <summary>마지막으로 알려진 시간·연결 상태.</summary>
        public MatchClock Clock { get; private set; }
        /// <summary>안내 문구 (마지막 사건).</summary>
        public string Status { get; private set; } = "상대를 기다리는 중";
        public bool GuestConnected => _guest != null && _guest.State == LinkState.Connected && _guestHello;

        public OnlineHost(HostSetup setup, ITransportListener listener, IAssetSource assetStore)
        {
            _setup = setup ?? throw new ArgumentNullException(nameof(setup));
            _listener = listener ?? throw new ArgumentNullException(nameof(listener));
            _assetStore = assetStore;
            var report = setup.Room.Check(setup.Deck, setup.Cards, setup.Rules, setup.Scores, setup.Registry);
            if (!report.IsValid)
                throw new InvalidOperationException("Host deck breaks the room rules: " + string.Join(" / ", report.Messages()));
            _hostCards = setup.Cards.Resolve(setup.Deck);
        }

        public void Update(int elapsedMs)
        {
            AcceptConnections();
            if (_guest != null && _guest.State == LinkState.Closed)
                GuestLost();
            if (_guest != null)
            {
                while (_guest != null && _guest.TryReceive(out var text))
                {
                    var message = NetCodec.Decode(text);
                    if (message != null)
                        Handle(message);
                }
            }
            if (Phase == HostPhase.Assets && _guestReady && _assets.Done)
                StartMatch();
            if (Phase == HostPhase.Playing)
                Match.Tick(elapsedMs);
        }

        /// <summary>방장(자리 0)의 명령.</summary>
        public ValidationResult Submit(Command command)
        {
            if (Phase != HostPhase.Playing)
                return ValidationResult.Fail("대전 중이 아닙니다.");
            return Match.Submit(HostSeat, command);
        }

        public void Close()
        {
            _listener.Close();
            _guest?.Close();
            Phase = HostPhase.Ended;
        }

        void AcceptConnections()
        {
            while (_listener.TryAccept(out var transport))
            {
                bool busy = _guest != null && _guest.State == LinkState.Connected;
                if (busy || Phase == HostPhase.Ended)
                {
                    transport.Send(NetCodec.Encode(new RejectMsg { Reason = "방이 찼습니다." }));
                    transport.Close();
                    continue;
                }
                _guest = transport;
                _guestHello = false;
            }
        }

        void GuestLost()
        {
            _guest = null;
            _guestHello = false;
            if (Phase == HostPhase.Playing)
            {
                Status = "상대 연결이 끊겼습니다";
                Match.SetConnected(GuestSeat, false);
            }
            else if (Phase != HostPhase.Ended)
            {
                Status = "상대가 나갔습니다. 다시 기다리는 중";
                ResetHandshake();
            }
        }

        void ResetHandshake()
        {
            Phase = HostPhase.Waiting;
            GuestName = null;
            GuestDeck = null;
            GuestCards = null;
            _guestReady = false;
            _coinSecret = null;
            _guestCoin = null;
            _assets = null;
        }

        void Send(NetMessage message) => _guest?.Send(NetCodec.Encode(message));

        void Reject(string reason)
        {
            Send(new RejectMsg { Reason = reason });
            _guest?.Close();
            _guest = null;
            _guestHello = false;
            Status = "접속 거절: " + reason;
        }

        void Handle(NetMessage message)
        {
            if (message is HelloMsg hello)
            {
                HandleHello(hello);
                return;
            }
            if (!_guestHello)
                return;
            if (_assets != null && _assets.Handle(message, _guest))
                return;

            switch (message)
            {
                case DeckSubmitMsg submit when Phase == HostPhase.Handshake:
                    HandleDeck(submit);
                    break;
                case AssetsReadyMsg _ when Phase == HostPhase.Assets:
                    _guestReady = true;
                    break;
                case CommandMsg command when Phase == HostPhase.Playing:
                    var cmd = command.Command?.ToCommand(GuestSeat);
                    var result = cmd == null ? ValidationResult.Fail("알 수 없는 명령입니다.") : Match.Submit(GuestSeat, cmd);
                    if (!result.IsValid)
                        Send(new CommandRejectedMsg { Seq = command.Seq, Reason = result.Reason });
                    break;
            }
        }

        void HandleHello(HelloMsg hello)
        {
            string mismatch = hello.Protocol != NetProtocol.Version ? "통신 버전이 다릅니다."
                : hello.App != _setup.AppVersion ? $"게임 버전이 다릅니다 (방장 {_setup.AppVersion}, 나 {hello.App})."
                : hello.CardSchema != CardSerializer.CurrentSchemaVersion ? "카드 데이터 버전이 다릅니다."
                : hello.ScoreTable != _setup.Scores.Version ? "점수표 버전이 다릅니다."
                : null;
            if (mismatch != null)
            {
                Reject(mismatch);
                return;
            }

            if (Phase == HostPhase.Playing || Phase == HostPhase.Ended)
            {
                if (Phase == HostPhase.Ended || hello.RejoinToken == null || hello.RejoinToken != _rejoinToken)
                {
                    Reject("이미 대전 중입니다.");
                    return;
                }
                _guestHello = true;
                SendResync();
                Status = "상대가 다시 접속했습니다";
                Match.SetConnected(GuestSeat, true);
                return;
            }

            _guestHello = true;
            GuestName = string.IsNullOrEmpty(hello.Name) ? "상대" : hello.Name;
            Phase = HostPhase.Handshake;
            Status = GuestName + " 접속, 덱 확인 중";
            _coinSecret = _setup.CoinSecret ?? CoinToss.NewSecret();
            Send(new WelcomeMsg
            {
                HostName = _setup.HostName, Room = _setup.Room, Rules = _setup.Rules,
                CoinCommit = CoinToss.Commit(_coinSecret),
            });
        }

        void HandleDeck(DeckSubmitMsg submit)
        {
            if (!CoinToss.IsValidSecret(submit.CoinSecret))
            {
                Reject("코인 토스 값이 잘못되었습니다.");
                ResetHandshake();
                return;
            }
            _guestCoin = submit.CoinSecret;

            var issues = new List<string>();
            var catalog = new CardCatalog();
            try
            {
                var cards = NetCodec.DecodeCards(submit.Cards);
                for (int i = 0; i < cards.Count; i++)
                {
                    if (catalog.TryGet(cards[i].Id, out _))
                        issues.Add($"카드 ID가 겹칩니다: {cards[i].Id}");
                    else
                        catalog.Add(cards[i]);
                }
            }
            catch (Exception e) when (e is FormatException || e is ArgumentException || e is Newtonsoft.Json.JsonException)
            {
                issues.Add("카드 데이터를 읽을 수 없습니다.");
            }
            if (issues.Count == 0)
                issues.AddRange(_setup.Room.Check(submit.Deck, catalog, _setup.Rules, _setup.Scores, _setup.Registry).Messages());
            if (issues.Count > 0)
            {
                Status = "상대 덱이 방 규칙에 맞지 않습니다";
                Send(new DeckRejectedMsg { Issues = issues });
                return;
            }

            GuestDeck = submit.Deck;
            GuestCards = catalog.Resolve(submit.Deck);
            Send(new DeckAcceptedMsg { Deck = _setup.Deck, Cards = NetCodec.EncodeCards(Distinct(_hostCards)) });
            Phase = HostPhase.Assets;
            Status = "카드 그림·사운드 교환 중";
            _assets = new AssetExchange(_assetStore, _setup.Limits);
            _assets.RequestMissing(catalog.Cards, _guest);
        }

        static List<CardDefinition> Distinct(IReadOnlyList<CardDefinition> cards)
        {
            var result = new List<CardDefinition>();
            for (int i = 0; i < cards.Count; i++)
            {
                if (!result.Contains(cards[i]))
                    result.Add(cards[i]);
            }
            return result;
        }

        void StartMatch()
        {
            FirstSeat = CoinToss.FirstSeat(_coinSecret, _guestCoin);
            Match = new HostMatch(_setup.Rules, _setup.Seed, _hostCards, GuestCards, _setup.Registry, firstPlayer: FirstSeat);
            Match.Updated += OnSeatUpdate;
            Match.ClockChanged += OnClockChanged;
            _rejoinToken = Guid.NewGuid().ToString("N");
            Send(new MatchStartMsg { Seat = GuestSeat, RejoinToken = _rejoinToken, CoinSecret = _coinSecret });
            Phase = HostPhase.Playing;
            Status = "대전 시작";
            Match.Start();
        }

        void OnSeatUpdate(SeatUpdate update)
        {
            Clock = update.Clock;
            if (Match.State.Phase == GamePhase.Finished)
                Phase = HostPhase.Ended;
            if (update.Seat == HostSeat)
            {
                LocalUpdates.Enqueue(update);
                return;
            }
            if (!GuestConnected)
                return;
            var message = new UpdateMsg
            {
                Events = EventCodec.EncodeAll(update.Events),
                Snapshot = StateSnapshot.Capture(update.After, Match.Catalog),
                Before = _sentFirstUpdate ? null : StateSnapshot.Capture(update.Before, Match.Catalog),
                Auto = update.Auto,
                AutoPlayer = update.AutoPlayer,
                End = update.End,
                Clock = update.Clock,
            };
            _sentFirstUpdate = true;
            if (Match.Catalog.Count > _catalogSent)
            {
                message.CatalogAdds = NetCodec.EncodeCards(Match.Catalog.Cards, _catalogSent);
                _catalogSent = Match.Catalog.Count;
            }
            Send(message);
        }

        void OnClockChanged(MatchClock clock)
        {
            Clock = clock;
            if (GuestConnected)
                Send(new ClockMsg { Clock = clock });
        }

        void SendResync()
        {
            var snapshot = StateSnapshot.Capture(PlayerView.Create(Match.State, GuestSeat), Match.Catalog);
            _catalogSent = Match.Catalog.Count;
            _sentFirstUpdate = true;
            Send(new ResyncMsg
            {
                Seat = GuestSeat,
                Rules = _setup.Rules,
                Catalog = NetCodec.EncodeCards(Match.Catalog.Cards),
                Snapshot = snapshot,
                End = Match.EndReason,
                Clock = Match.Clock(),
            });
        }
    }
}
