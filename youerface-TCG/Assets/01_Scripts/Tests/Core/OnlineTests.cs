using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TCG.Core.AI;
using TCG.Core.Commands;
using TCG.Core.Data;
using TCG.Core.Events;
using TCG.Core.Net;
using TCG.Core.Rules;
using TCG.Core.Serialization;
using TCG.Core.State;

namespace TCG.Tests.Core
{
    /// <summary>메모리 에셋 저장소 (온라인 테스트용).</summary>
    internal sealed class MemoryAssetSource : IAssetSource
    {
        readonly Dictionary<string, (byte[] data, string ext)> _files = new Dictionary<string, (byte[], string)>();

        public bool Has(string hash) => _files.ContainsKey(hash);

        public bool TryRead(string hash, out byte[] data, out string extension)
        {
            if (_files.TryGetValue(hash, out var file))
            {
                data = file.data;
                extension = file.ext;
                return true;
            }
            data = null;
            extension = null;
            return false;
        }

        public string Write(byte[] data, string extension)
        {
            string hash = AssetHash.Compute(data);
            _files[hash] = (data, extension);
            return hash;
        }
    }

    /// <summary>M6 온라인: 시야 필터·직렬화·호스트 대전 타이머·핸드셰이크·가짜 연결 대전.</summary>
    public class OnlineTests
    {
        // ------------------------------------------------------------------ 도우미

        static (CardCatalog catalog, DeckDefinition deck0, DeckDefinition deck1) Samples()
        {
            var decks = SampleDataTests.LoadDecks();
            return (SampleDataTests.LoadCatalog(), decks[0], decks[1]);
        }

        static HostSetup HostSetupFor(CardCatalog catalog, DeckDefinition deck, int seed = 7) => new HostSetup
        {
            AppVersion = "1.0",
            HostName = "방장",
            Deck = deck,
            Cards = catalog,
            Seed = seed,
        };

        static GuestSetup GuestSetupFor(CardCatalog catalog, DeckDefinition deck) => new GuestSetup
        {
            AppVersion = "1.0",
            Name = "손님",
            Deck = deck,
            Cards = catalog,
        };

        static void Pump(OnlineHost host, OnlineGuest guest, int rounds = 20, int ms = 0)
        {
            for (int i = 0; i < rounds; i++)
            {
                host.Update(ms);
                guest.Update(ms);
            }
        }

        static HostMatch NewMatch(int seed = 3, RuleConfig rules = null)
        {
            var match = new HostMatch(rules ?? RuleConfig.CreateDefault(), seed, ReplayTests.MixedDeck(), ReplayTests.MixedDeck());
            match.Start();
            return match;
        }

        static IEnumerable<Entity> All(GameState state)
        {
            for (int p = 0; p < GameState.PlayerCount; p++)
                foreach (var zone in state.GetPlayer(p).Zones)
                    foreach (var e in zone.Entities)
                        yield return e;
        }

        // ------------------------------------------------------------------ 시야·직렬화

        [Test]
        public void StartGame_EntityIdsFollowShuffledDeckOrder()
        {
            var deck = ReplayTests.MixedDeck();
            var state = new GameState(RuleConfig.CreateDefault(), 11);
            new GameEngine().StartGame(state, deck, deck);
            // 카드 ID 순서 = 섞인 순서이므로, ID 순으로 늘어놓은 정의가 원래 덱 목록 순서와 같지 않다.
            var byId = All(state).Where(e => e.OwnerIndex == 0).OrderBy(e => e.Id).Select(e => e.Definition.Id).ToList();
            var original = deck.Select(c => c.Id).ToList();
            Assert.That(byId, Is.Not.EqualTo(original));
            var remaining = state.GetPlayer(0).Deck.Entities.Select(e => e.Id).ToList();
            Assert.That(remaining, Is.Ordered, "덱 안의 카드는 ID 순서대로 놓인다");
        }

        [Test]
        public void PlayerView_HidesDecksAndOpponentHand()
        {
            var match = NewMatch();
            var view = PlayerView.Create(match.State, 1);
            foreach (var e in All(view))
            {
                bool hidden = e.Location == ZoneType.Deck || (e.Location == ZoneType.Hand && e.OwnerIndex == 0);
                Assert.That(HiddenCard.Is(e), Is.EqualTo(hidden), $"entity {e.Id} in {e.Location} of {e.OwnerIndex}");
            }
            Assert.That(view.GetPlayer(0).Hand.Count, Is.EqualTo(match.State.GetPlayer(0).Hand.Count));
            Assert.That(view.Rng.SaveState(), Is.Not.EqualTo(match.State.Rng.SaveState()));
            Assert.That(HiddenCard.Is(match.State.GetPlayer(0).Hand.Entities[0]), Is.False, "진짜 상태는 그대로");
        }

        [Test]
        public void Snapshot_RoundTripsThroughJson()
        {
            var match = NewMatch(5);
            var engine = match.Engine;
            var ai = AiPlayers.Create(AiDifficulty.Easy, 9, engine);
            for (int i = 0; i < 40 && match.InProgress; i++)
                match.Submit(match.Mover, ai.ChooseCommand(match.State, match.Mover));

            for (int seat = 0; seat < 2; seat++)
            {
                var view = PlayerView.Create(match.State, seat);
                var snapshot = StateSnapshot.Capture(view, match.Catalog);
                var text = NetCodec.Encode(new ResyncMsg { Snapshot = snapshot, Catalog = NetCodec.EncodeCards(match.Catalog.Cards) });
                var back = (ResyncMsg)NetCodec.Decode(text);
                var restored = back.Snapshot.ToState(RuleConfig.CreateDefault(), NetCodec.DecodeCards(back.Catalog));
                Assert.That(StateHasher.Compute(restored), Is.EqualTo(StateHasher.Compute(view)), $"seat {seat}");
            }
        }

        [Test]
        public void EventCodec_RoundTripsEveryEventOfAGame()
        {
            var match = NewMatch(8);
            var events = new List<GameEvent>();
            match.Updated += u => { if (u.Seat == 0) events.AddRange(u.Events); };
            var ai = AiPlayers.Create(AiDifficulty.Easy, 4, match.Engine);
            for (int i = 0; i < 3000 && match.InProgress; i++)
                match.Submit(match.Mover, ai.ChooseCommand(match.State, match.Mover));

            Assert.That(match.InProgress, Is.False);
            Assert.That(events.Select(e => e.GetType()).Distinct().Count(), Is.GreaterThan(10));
            foreach (var e in events)
            {
                var json = EventCodec.Encode(e);
                var back = EventCodec.Decode(json);
                Assert.That(back.GetType(), Is.EqualTo(e.GetType()));
                Assert.That(EventCodec.Encode(back).ToString(), Is.EqualTo(json.ToString()));
            }
        }

        [Test]
        public void CommandData_RoundTripsAndUsesReceiverSeat()
        {
            var commands = new Command[]
            {
                new PlayCardCommand(0, 5, 7, 1), new AttackCommand(0, 4, 2), new EndTurnCommand(0),
                new ChainActivateCommand(0, 3), new PassChainCommand(0), new ConcedeCommand(0),
            };
            foreach (var c in commands)
            {
                var msg = (CommandMsg)NetCodec.Decode(NetCodec.Encode(new CommandMsg { Seq = 1, Command = CommandData.From(c) }));
                var back = msg.Command.ToCommand(1);
                Assert.That(back.GetType(), Is.EqualTo(c.GetType()));
                Assert.That(back.PlayerIndex, Is.EqualTo(1));
            }
            Assert.That(new CommandData { Kind = "play" }.ToCommand(1), Is.Null);
        }

        [Test]
        public void Concede_EndsGameForEitherPlayerEvenDuringDecision()
        {
            var match = NewMatch();
            int waiting = 1 - match.State.ActivePlayerIndex;
            Assert.That(match.Submit(waiting, new ConcedeCommand(waiting)).IsValid, Is.True);
            Assert.That(match.State.Phase, Is.EqualTo(GamePhase.Finished));
            Assert.That(match.State.WinnerIndex, Is.EqualTo(1 - waiting));
            Assert.That(match.EndReason, Is.EqualTo(MatchEndReason.Conceded));
        }

        // ------------------------------------------------------------------ 방 규칙

        [Test]
        public void RoomRules_RejectsCardsOverScoreLimit()
        {
            var (catalog, deck, _) = Samples();
            var rules = RuleConfig.CreateDefault();
            Assert.That(new RoomRules().Check(deck, catalog, rules).IsValid, Is.True);

            var report = DeckValidator.Check(deck, catalog, rules);
            var room = new RoomRules { MaxCardScore = report.MaxScore - 1 };
            var limited = room.Check(deck, catalog, rules);
            Assert.That(limited.IsValid, Is.False);
            Assert.That(limited.Issues.All(i => i.Kind == DeckIssueKind.OverScoreLimit), Is.True);
            Assert.That(new RoomRules { MaxCardScore = report.MaxScore }.Check(deck, catalog, rules).IsValid, Is.True);
        }

        // ------------------------------------------------------------------ 타이머·끊김

        [Test]
        public void TurnTimeout_EndsTurnAndThreeInARowLoses()
        {
            var match = NewMatch();
            var rules = match.Rules;
            int first = match.State.ActivePlayerIndex;
            int turn = match.State.TurnNumber;

            match.Tick(rules.TurnTimeLimitSeconds * 1000 - 1);
            Assert.That(match.State.TurnNumber, Is.EqualTo(turn), "아직 시간 안");
            match.Tick(1);
            Assert.That(match.State.ActivePlayerIndex, Is.EqualTo(1 - first), "시간 초과 → 자동 턴 종료");
            Assert.That(match.Timeouts[first], Is.EqualTo(1));

            // 상대는 직접 턴을 끝내고, first는 계속 시간 초과.
            for (int i = 0; i < rules.MaxConsecutiveTimeouts - 1; i++)
            {
                int other = 1 - first;
                ResolveDecisions(match, other);
                Assert.That(match.Submit(other, new EndTurnCommand(other)).IsValid, Is.True);
                ResolveDecisions(match, -1);
                TickUntilTurnChanges(match);
            }
            Assert.That(match.State.Phase, Is.EqualTo(GamePhase.Finished));
            Assert.That(match.State.WinnerIndex, Is.EqualTo(1 - first));
            Assert.That(match.EndReason, Is.EqualTo(MatchEndReason.Timeouts));
        }

        [Test]
        public void TimeoutCount_ResetsWhenPlayerActs()
        {
            var match = NewMatch();
            int first = match.State.ActivePlayerIndex;
            match.Tick(match.Rules.TurnTimeLimitSeconds * 1000);
            Assert.That(match.Timeouts[first], Is.EqualTo(1));
            int other = 1 - first;
            ResolveDecisions(match, other);
            match.Submit(other, new EndTurnCommand(other));
            ResolveDecisions(match, -1);
            Assert.That(match.State.ActivePlayerIndex, Is.EqualTo(first));
            ResolveDecisions(match, first);
            Assert.That(match.Submit(first, new EndTurnCommand(first)).IsValid, Is.True);
            Assert.That(match.Timeouts[first], Is.EqualTo(0));
        }

        [Test]
        public void Disconnected_AutoActsAfterWaitAndLosesAfterLimit_CountedSeparately()
        {
            var match = NewMatch();
            var rules = match.Rules;
            int first = match.State.ActivePlayerIndex;
            match.SetConnected(first, false);

            match.Tick(rules.DisconnectedActionWaitSeconds * 1000);
            Assert.That(match.State.ActivePlayerIndex, Is.EqualTo(1 - first), "끊긴 플레이어 차례는 기다린 뒤 자동 종료");
            Assert.That(match.DisconnectAutos[first], Is.EqualTo(1));
            Assert.That(match.Timeouts[first], Is.EqualTo(0), "시간 초과와 따로 센다");

            int guard = 0;
            while (match.InProgress && guard++ < 200)
            {
                int mover = match.Mover;
                if (mover == first)
                    match.Tick(1000);
                else
                    match.Submit(mover, HostMatch.AutoCommand(match.State, mover));
            }
            Assert.That(match.State.WinnerIndex, Is.EqualTo(1 - first));
            Assert.That(match.EndReason, Is.EqualTo(MatchEndReason.Disconnected));
            Assert.That(match.DisconnectAutos[first], Is.EqualTo(rules.MaxDisconnectedAutoActions));
        }

        static void ResolveDecisions(HostMatch match, int onlyFor)
        {
            for (int i = 0; i < 50 && match.InProgress && match.State.Decision != null; i++)
            {
                int p = match.State.Decision.PlayerIndex;
                if (onlyFor >= 0 && p != onlyFor)
                    return;
                match.Submit(p, HostMatch.AutoCommand(match.State, p));
            }
        }

        static void TickUntilTurnChanges(HostMatch match)
        {
            int turn = match.State.TurnNumber;
            for (int i = 0; i < 200 && match.InProgress && match.State.TurnNumber == turn; i++)
                match.Tick(1000);
        }

        // ------------------------------------------------------------------ 핸드셰이크·가짜 연결

        [Test]
        public void Handshake_VersionMismatchIsRejected()
        {
            var (catalog, deck0, deck1) = Samples();
            var listener = new LoopbackListener();
            var host = new OnlineHost(HostSetupFor(catalog, deck0), listener, new MemoryAssetSource());
            var setup = GuestSetupFor(catalog, deck1);
            setup.AppVersion = "0.9";
            var guest = new OnlineGuest(setup, listener.Connect(), new MemoryAssetSource());
            Pump(host, guest);
            Assert.That(guest.Phase, Is.EqualTo(GuestPhase.Rejected));
            StringAssert.Contains("게임 버전", guest.RejectReason);
            Assert.That(host.Phase, Is.EqualTo(HostPhase.Waiting));
        }

        [Test]
        public void Handshake_DeckOverRoomLimitIsRejectedThenAnotherDeckPasses()
        {
            var (samples, deck0, deck1) = Samples();
            // 샘플 카드 + 아주 센 카드. 상한 = 샘플 덱들의 최고 점수 → 센 카드를 넣은 덱만 걸린다.
            var catalog = new CardCatalog();
            foreach (var card in samples.Cards)
                catalog.Add(card);
            catalog.Add(new CardDefinition { Id = "c_strong", Name = "센 카드", Type = CardType.Minion, Cost = 1, Attack = 15, Health = 15 });
            var rules = RuleConfig.CreateDefault();
            int limit = Math.Max(DeckValidator.Check(deck0, catalog, rules).MaxScore, DeckValidator.Check(deck1, catalog, rules).MaxScore);
            var strong = new DeckDefinition { Id = "d_strong", Name = "센 덱", Cards = new List<string>(deck1.Cards) };
            strong.Cards[0] = "c_strong";

            var listener = new LoopbackListener();
            var hostSetup = HostSetupFor(catalog, deck0);
            hostSetup.Room = new RoomRules { MaxCardScore = limit };
            var host = new OnlineHost(hostSetup, listener, new MemoryAssetSource());
            var guest = new OnlineGuest(GuestSetupFor(catalog, strong), listener.Connect(), new MemoryAssetSource());
            Pump(host, guest);
            Assert.That(guest.Phase, Is.EqualTo(GuestPhase.DeckRejected));
            Assert.That(guest.Room.MaxCardScore, Is.EqualTo(limit));
            Assert.That(guest.DeckIssues.Count, Is.EqualTo(1));
            StringAssert.Contains("센 카드", guest.DeckIssues[0]);

            guest.SubmitDeck(deck1, catalog);
            Pump(host, guest);
            Assert.That(guest.Phase, Is.EqualTo(GuestPhase.Playing));
            Assert.That(host.Phase, Is.EqualTo(HostPhase.Playing));
        }

        [Test]
        public void Handshake_SendsOnlyMissingAssetsInChunks()
        {
            var (samples, deck0, deck1) = Samples();
            var hostStore = new MemoryAssetSource();
            var rng = new System.Random(1);
            var art = new byte[NetProtocol.AssetChunkBytes * 2 + 100];
            rng.NextBytes(art);
            var sound = new byte[1000];
            rng.NextBytes(sound);
            string artHash = hostStore.Write(art, "jpg");
            string soundHash = hostStore.Write(sound, "wav");

            // 방장 덱 첫 카드에 그림·사운드를 붙인 카드 모음.
            var hostCatalog = new CardCatalog();
            string decorated = deck0.Cards[0];
            foreach (var card in samples.Cards)
            {
                var copy = CardSerializer.Deserialize(CardSerializer.Serialize(card));
                if (copy.Id == decorated)
                {
                    copy.Art.Image = artHash;
                    copy.Sounds.Enter = soundHash;
                }
                hostCatalog.Add(copy);
            }

            var guestStore = new MemoryAssetSource();
            guestStore.Write(sound, "wav"); // 이미 있는 것은 다시 받지 않는다.
            var listener = new LoopbackListener();
            var host = new OnlineHost(HostSetupFor(hostCatalog, deck0), listener, hostStore);
            var guest = new OnlineGuest(GuestSetupFor(samples, deck1), listener.Connect(), guestStore);
            Pump(host, guest);

            Assert.That(guest.Phase, Is.EqualTo(GuestPhase.Playing));
            Assert.That(guestStore.TryRead(artHash, out var got, out var ext), Is.True);
            Assert.That(got, Is.EqualTo(art));
            Assert.That(ext, Is.EqualTo("jpg"));
            Assert.That(guest.Assets.Received, Is.EqualTo(1));
            Assert.That(guest.HostCards.First(c => c.Id == decorated).Art.Image, Is.EqualTo(artHash));
        }

        [Test]
        public void Loopback_FullMatch_GuestViewMatchesHostAndHidesSecrets()
        {
            var (catalog, deck0, deck1) = Samples();
            var listener = new LoopbackListener();
            var host = new OnlineHost(HostSetupFor(catalog, deck0, seed: 21), listener, new MemoryAssetSource());
            var guestLink = listener.Connect();
            var guest = new OnlineGuest(GuestSetupFor(catalog, deck1), guestLink, new MemoryAssetSource());
            Pump(host, guest);
            Assert.That(host.Phase, Is.EqualTo(HostPhase.Playing));
            Assert.That(guest.Phase, Is.EqualTo(GuestPhase.Playing));

            var engine = new GameEngine();
            var hostAi = AiPlayers.Create(AiDifficulty.Easy, 1, engine);
            var guestAi = AiPlayers.Create(AiDifficulty.Easy, 2, engine);
            GameState hostView = null;
            int updates = 0;
            for (int step = 0; step < 4000 && host.Phase == HostPhase.Playing; step++)
            {
                host.Update(16);
                guest.Update(16);
                while (host.LocalUpdates.Count > 0)
                    hostView = host.LocalUpdates.Dequeue().After;
                while (guest.Updates.Count > 0)
                {
                    var u = guest.Updates.Dequeue();
                    updates++;
                    // 게스트가 다시 만든 시야 = 호스트가 만든 게스트 시야.
                    Assert.That(StateHasher.Compute(u.After), Is.EqualTo(StateHasher.Compute(PlayerView.Create(host.Match.State, 1))));
                    foreach (var e in u.After.GetPlayer(0).Hand.Entities)
                        Assert.That(HiddenCard.Is(e), Is.True, "상대 손패는 가려짐");
                    foreach (var e in u.After.GetPlayer(1).Deck.Entities)
                        Assert.That(HiddenCard.Is(e), Is.True, "내 덱도 가려짐");
                }

                if (host.Phase != HostPhase.Playing)
                    break;
                int mover = host.Match.Mover;
                if (mover == 0 && hostView != null)
                    Assert.That(host.Submit(hostAi.ChooseCommand(hostView, 0)).IsValid, Is.True);
                else if (mover == 1 && guest.View != null && guest.Updates.Count == 0 && guest.View.Phase == GamePhase.InProgress
                         && (guest.View.Decision?.PlayerIndex ?? guest.View.ActivePlayerIndex) == 1)
                    guest.Submit(guestAi.ChooseCommand(guest.View, 1));
                Assert.That(guest.LastCommandRejection, Is.Null);
            }
            Pump(host, guest, 3);
            while (guest.Updates.Count > 0)
                guest.Updates.Dequeue();
            Assert.That(host.Match.State.Phase, Is.EqualTo(GamePhase.Finished));
            Assert.That(guest.Phase, Is.EqualTo(GuestPhase.Ended));
            Assert.That(guest.View.WinnerIndex, Is.EqualTo(host.Match.State.WinnerIndex));
            Assert.That(updates, Is.GreaterThan(20));
        }

        [Test]
        public void Loopback_GuestDropsAndRejoins_ResyncsAndContinues()
        {
            var (catalog, deck0, deck1) = Samples();
            var listener = new LoopbackListener();
            var host = new OnlineHost(HostSetupFor(catalog, deck0), listener, new MemoryAssetSource());
            var link = listener.Connect();
            var guest = new OnlineGuest(GuestSetupFor(catalog, deck1), link, new MemoryAssetSource());
            Pump(host, guest);
            Assert.That(guest.Phase, Is.EqualTo(GuestPhase.Playing));

            link.Drop();
            Pump(host, guest, 2, 100);
            Assert.That(guest.Phase, Is.EqualTo(GuestPhase.Reconnecting));
            Assert.That(host.Match.Connected[1], Is.False);
            Assert.That(host.Clock.Connected[1], Is.False);

            // 방장 차례면 끝내서 게스트 차례에 끊긴 동안 자동 처리가 일어나게.
            if (host.Match.Mover == 0)
                host.Submit(HostMatch.AutoCommand(host.Match.State, 0));
            host.Update(host.Match.Rules.DisconnectedActionWaitSeconds * 1000);
            Assert.That(host.Match.DisconnectAutos[1], Is.EqualTo(1));

            // 다른 사람이 끼어들 수 없다.
            var intruder = new OnlineGuest(GuestSetupFor(catalog, deck1), listener.Connect(), new MemoryAssetSource());
            Pump(host, intruder, 5);
            Assert.That(intruder.Phase, Is.EqualTo(GuestPhase.Rejected));

            guest.Rejoin(listener.Connect());
            while (guest.Updates.Count > 0)
                guest.Updates.Dequeue();
            Pump(host, guest);
            Assert.That(guest.Phase, Is.EqualTo(GuestPhase.Playing));
            Assert.That(host.Match.Connected[1], Is.True);
            var resync = guest.Updates.Last(u => u.Resync);
            Assert.That(StateHasher.Compute(resync.After), Is.EqualTo(StateHasher.Compute(PlayerView.Create(host.Match.State, 1))));

            // 다시 이어서 항복까지.
            guest.Submit(new ConcedeCommand(1));
            Pump(host, guest);
            Assert.That(host.Match.EndReason, Is.EqualTo(MatchEndReason.Conceded));
            Assert.That(guest.Phase, Is.EqualTo(GuestPhase.Ended));
            Assert.That(guest.View.WinnerIndex, Is.EqualTo(0));
        }

        [Test]
        public void Loopback_HostNeverReturns_GuestEndsAfterWait()
        {
            var (catalog, deck0, deck1) = Samples();
            var listener = new LoopbackListener();
            var host = new OnlineHost(HostSetupFor(catalog, deck0), listener, new MemoryAssetSource());
            var link = listener.Connect();
            var guest = new OnlineGuest(GuestSetupFor(catalog, deck1), link, new MemoryAssetSource());
            Pump(host, guest);
            link.Drop();
            int wait = guest.Rules.HostReconnectWaitSeconds * 1000;
            guest.Update(0);
            guest.Update(wait - 1);
            Assert.That(guest.Phase, Is.EqualTo(GuestPhase.Reconnecting));
            guest.Update(1);
            Assert.That(guest.Phase, Is.EqualTo(GuestPhase.Ended));
            Assert.That(guest.EndReason, Is.EqualTo(MatchEndReason.HostLost));
        }
    }
}
