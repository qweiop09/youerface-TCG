using System.Collections.Generic;
using NUnit.Framework;
using TCG.Core.Data;
using TCG.Core.Events;
using TCG.Core.Net;
using TCG.Core.Rules;
using TCG.Core.State;

namespace TCG.Tests.Core
{
    /// <summary>M5.5-2: 선공 지정, 후공 보정(최대 코스트·손패), 온라인 코인 토스(양쪽 비밀값 합치기).</summary>
    public class CoinTossTests
    {
        static List<CardDefinition> Deck(int count)
        {
            var deck = new List<CardDefinition>();
            for (int i = 0; i < count; i++)
                deck.Add(TestCards.Minion("c_" + i, 1 + i % 5, 1 + i % 3, 1 + i % 4));
            return deck;
        }

        // ------------------------------------------------------------------ 후공 보정 (규칙)

        [Test]
        public void SecondPlayer_StartsWithRuleConfigMaxCost_ThenGrows()
        {
            var rules = RuleConfig.CreateDefault();
            rules.SecondPlayerStartingMaxCost = 3;
            var state = new GameState(rules, 11);
            var engine = new GameEngine();
            engine.StartGame(state, Deck(30), Deck(30));
            int first = state.ActivePlayerIndex;
            var second = state.GetPlayer(1 - first);
            Assert.That(state.GetPlayer(first).MaxCost, Is.EqualTo(rules.StartingMaxCost));
            Assert.That(second.MaxCost, Is.EqualTo(3), "후공은 대전 시작부터 SecondPlayerStartingMaxCost");
            Assert.That(second.AvailableCost, Is.EqualTo(3));

            engine.Apply(state, new TCG.Core.Commands.EndTurnCommand(first));
            Assert.That(second.MaxCost, Is.EqualTo(3), "후공 첫 턴에도 늘지 않음");
            Assert.That(second.AvailableCost, Is.EqualTo(3));

            engine.Apply(state, new TCG.Core.Commands.EndTurnCommand(1 - first));
            Assert.That(state.GetPlayer(first).MaxCost, Is.EqualTo(rules.StartingMaxCost + rules.MaxCostGainPerTurn));
            engine.Apply(state, new TCG.Core.Commands.EndTurnCommand(first));
            Assert.That(second.MaxCost, Is.EqualTo(3 + rules.MaxCostGainPerTurn));
        }

        [Test]
        public void Defaults_NoBonusCard_SecondPlayerOneMoreCard()
        {
            var rules = RuleConfig.CreateDefault();
            Assert.That(rules.SecondPlayerGetsBonusCard, Is.False, "보정 카드 없음 (Q14)");
            Assert.That(rules.SecondPlayerStartingMaxCost, Is.EqualTo(2));
            Assert.That(rules.SecondPlayerStartingHand - rules.FirstPlayerStartingHand, Is.EqualTo(1));

            var state = new GameState(rules, 3);
            new GameEngine().StartGame(state, Deck(30), Deck(30), TestCards.Spell("c_coin", 0));
            var second = state.GetOpponent(state.ActivePlayerIndex);
            Assert.That(second.Hand.Count, Is.EqualTo(rules.SecondPlayerStartingHand), "플래그가 꺼져 있으면 보정 카드를 넘겨도 안 줌");
        }

        // ------------------------------------------------------------------ 선공 지정

        [TestCase(0)]
        [TestCase(1)]
        public void StartGame_ForcedFirstPlayer(int first)
        {
            for (int seed = 1; seed <= 6; seed++)
            {
                var state = new GameState(RuleConfig.CreateDefault(), seed);
                var events = new GameEngine().StartGame(state, Deck(30), Deck(30), firstPlayer: first);
                Assert.That(state.ActivePlayerIndex, Is.EqualTo(first));
                Assert.That(((GameStartedEvent)events[0]).FirstPlayerIndex, Is.EqualTo(first));
                Assert.That(state.GetPlayer(first).MaxCost, Is.EqualTo(1));
                Assert.That(state.GetPlayer(1 - first).Hand.Count, Is.EqualTo(4));
            }
        }

        [Test]
        public void StartGame_ForcedFirstPlayer_SameShuffleAsSeed()
        {
            // 선공을 지정해도 난수 사용량이 같아 덱 순서는 시드로만 정해진다.
            var a = new GameState(RuleConfig.CreateDefault(), 42);
            var b = new GameState(RuleConfig.CreateDefault(), 42);
            new GameEngine().StartGame(a, Deck(30), Deck(30), firstPlayer: 0);
            new GameEngine().StartGame(b, Deck(30), Deck(30), firstPlayer: 1);
            var deckA = a.GetPlayer(0).Deck.Entities;
            var deckB = b.GetPlayer(0).Deck.Entities;
            // 뽑은 장수가 달라(선공 3+1, 후공 4) 아래쪽 25장이 같은지 본다.
            for (int i = 0; i < 25; i++)
                Assert.That(deckA[deckA.Count - 1 - i].Definition.Id, Is.EqualTo(deckB[deckB.Count - 1 - i].Definition.Id));
        }

        [Test]
        public void StartGame_InvalidFirstPlayerThrows()
        {
            var state = new GameState(RuleConfig.CreateDefault(), 1);
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new GameEngine().StartGame(state, Deck(30), Deck(30), firstPlayer: 2));
        }

        // ------------------------------------------------------------------ 코인 토스 계산

        [Test]
        public void Commit_VerifiesOnlyTheSameSecret()
        {
            string secret = CoinToss.NewSecret();
            Assert.That(secret.Length, Is.EqualTo(32));
            Assert.That(CoinToss.NewSecret(), Is.Not.EqualTo(secret));
            string commit = CoinToss.Commit(secret);
            Assert.That(CoinToss.Verify(commit, secret), Is.True);
            Assert.That(CoinToss.Verify(commit, secret + "0"), Is.False);
            Assert.That(CoinToss.Verify(null, secret), Is.False);
            Assert.That(CoinToss.Verify(commit, null), Is.False);
            Assert.That(CoinToss.IsValidSecret(new string('a', CoinToss.MaxSecretLength + 1)), Is.False);
        }

        [Test]
        public void FirstSeat_DependsOnBothSecrets_AndIsRoughlyFair()
        {
            Assert.That(CoinToss.FirstSeat("host", "guest"), Is.EqualTo(CoinToss.FirstSeat("host", "guest")));
            int[] count = new int[2];
            for (int i = 0; i < 400; i++)
                count[CoinToss.FirstSeat("host-fixed", "guest-" + i)]++;
            // 방장 값을 고정해도 게스트 값에 따라 양쪽이 고르게 나온다.
            Assert.That(count[0], Is.InRange(140, 260));
            Assert.That(count[1], Is.InRange(140, 260));
        }

        // ------------------------------------------------------------------ 온라인 핸드셰이크

        static (OnlineHost host, OnlineGuest guest) Connect(string hostSecret, string guestSecret)
        {
            var catalog = SampleDataTests.LoadCatalog();
            var decks = SampleDataTests.LoadDecks();
            var listener = new LoopbackListener();
            var host = new OnlineHost(new HostSetup
            {
                AppVersion = "1.0", HostName = "방장", Deck = decks[0], Cards = catalog, Seed = 5, CoinSecret = hostSecret,
            }, listener, new MemoryAssetSource());
            var guest = new OnlineGuest(new GuestSetup
            {
                AppVersion = "1.0", Name = "손님", Deck = decks[1], Cards = catalog, CoinSecret = guestSecret,
            }, listener.Connect(), new MemoryAssetSource());
            for (int i = 0; i < 20; i++)
            {
                host.Update(0);
                guest.Update(0);
            }
            return (host, guest);
        }

        /// <summary>방장 값 "h"에 대해 선공 자리가 seat가 되는 게스트 값.</summary>
        static string GuestSecretFor(int seat)
        {
            for (int i = 0; ; i++)
            {
                if (CoinToss.FirstSeat("h", "g" + i) == seat)
                    return "g" + i;
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        public void Online_BothSidesAgreeOnFirstSeat(int seat)
        {
            var (host, guest) = Connect("h", GuestSecretFor(seat));
            Assert.That(host.Phase, Is.EqualTo(HostPhase.Playing));
            Assert.That(guest.Phase, Is.EqualTo(GuestPhase.Playing), guest.RejectReason);
            Assert.That(host.FirstSeat, Is.EqualTo(seat));
            Assert.That(guest.FirstSeat, Is.EqualTo(seat));
            Assert.That(host.Match.State.ActivePlayerIndex, Is.EqualTo(seat));
            Assert.That(guest.View.ActivePlayerIndex, Is.EqualTo(seat));
            Assert.That(host.Match.State.GetPlayer(1 - seat).Hand.Count, Is.EqualTo(host.Match.Rules.SecondPlayerStartingHand));
        }

        [Test]
        public void Online_RandomSecrets_StillAgree()
        {
            var (host, guest) = Connect(null, null);
            Assert.That(guest.Phase, Is.EqualTo(GuestPhase.Playing), guest.RejectReason);
            Assert.That(guest.FirstSeat, Is.EqualTo(host.FirstSeat));
        }

        [Test]
        public void Online_InvalidGuestSecretIsRejected()
        {
            var (host, guest) = Connect("h", "");
            Assert.That(guest.Phase, Is.EqualTo(GuestPhase.Rejected));
            StringAssert.Contains("코인", guest.RejectReason);
            Assert.That(host.Phase, Is.EqualTo(HostPhase.Waiting));
        }

        /// <summary>게스트 앞에 가짜 방장을 세운다: Welcome(약속) 뒤 원하는 메시지를 보낸다.</summary>
        static OnlineGuest FakeHost(string committedSecret, out LoopbackTransport hostEnd)
        {
            LoopbackTransport.CreatePair(out hostEnd, out var guestEnd);
            var catalog = SampleDataTests.LoadCatalog();
            var guest = new OnlineGuest(new GuestSetup
            {
                AppVersion = "1.0", Name = "손님", Deck = SampleDataTests.LoadDecks()[1], Cards = catalog, CoinSecret = "g",
            }, guestEnd, new MemoryAssetSource());
            guest.Update(0);
            hostEnd.Send(NetCodec.Encode(new WelcomeMsg
            {
                HostName = "방장", Room = new RoomRules(), Rules = RuleConfig.CreateDefault(), CoinCommit = CoinToss.Commit(committedSecret),
            }));
            guest.Update(0);
            return guest;
        }

        [Test]
        public void Online_HostRevealingDifferentSecretIsDetected()
        {
            var guest = FakeHost("h", out var hostEnd);
            // 게스트 값을 본 뒤 자기에게 유리한 값으로 바꿔 공개 → 약속과 달라 거절.
            hostEnd.Send(NetCodec.Encode(new MatchStartMsg { Seat = 1, RejoinToken = "t", CoinSecret = "h2" }));
            guest.Update(0);
            Assert.That(guest.Phase, Is.EqualTo(GuestPhase.Rejected));
            StringAssert.Contains("코인", guest.RejectReason);
        }

        [Test]
        public void Online_HostStartingWithWrongFirstSeatIsDetected()
        {
            var guest = FakeHost("h", out var hostEnd);
            hostEnd.Send(NetCodec.Encode(new MatchStartMsg { Seat = 1, RejoinToken = "t", CoinSecret = "h" }));
            guest.Update(0);
            Assert.That(guest.Phase, Is.EqualTo(GuestPhase.Playing));
            int fair = CoinToss.FirstSeat("h", "g");
            Assert.That(guest.FirstSeat, Is.EqualTo(fair));

            // 약속은 지켰지만 엔진을 다른 선공으로 시작 → 첫 변화에서 걸림.
            hostEnd.Send(NetCodec.Encode(new UpdateMsg
            {
                Events = EventCodec.EncodeAll(new GameEvent[] { new GameStartedEvent(1 - fair) }),
            }));
            guest.Update(0);
            Assert.That(guest.Phase, Is.EqualTo(GuestPhase.Rejected));
            Assert.That(guest.Updates.Count, Is.EqualTo(0));
        }

        [Test]
        public void ProtocolVersionBumpedForCoinToss()
        {
            Assert.That(NetProtocol.Version, Is.GreaterThanOrEqualTo(5));
        }
    }
}
