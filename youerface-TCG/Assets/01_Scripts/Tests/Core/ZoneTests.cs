using System;
using NUnit.Framework;
using TCG.Core.Data;
using TCG.Core.State;

namespace TCG.Tests.Core
{
    public class ZoneTests
    {
        GameState _state;

        [SetUp]
        public void SetUp()
        {
            _state = new GameState(RuleConfig.CreateDefault(), seed: 1);
        }

        Entity NewMinion(int owner = 0) => _state.CreateEntity(owner, TestCards.Minion());

        [Test]
        public void PlayerZones_UseConfirmedSlotCounts()
        {
            var player = _state.GetPlayer(0);
            Assert.That(player.Minions.Capacity, Is.EqualTo(3));
            Assert.That(player.Field.Capacity, Is.EqualTo(1));
            Assert.That(player.Companions.Capacity, Is.EqualTo(4));
            Assert.That(player.Summons.Capacity, Is.EqualTo(4));
            Assert.That(player.Hand.Capacity, Is.EqualTo(_state.Rules.HandLimit));
            Assert.That(player.Deck.IsUnlimited, Is.True);
            Assert.That(player.Graveyard.IsUnlimited, Is.True);
        }

        [TestCase(ZoneType.Minion, 3)]
        [TestCase(ZoneType.Field, 1)]
        [TestCase(ZoneType.Companion, 4)]
        [TestCase(ZoneType.Summon, 4)]
        public void Zone_RejectsAddBeyondCapacity(ZoneType type, int capacity)
        {
            var zone = _state.GetPlayer(0).GetZone(type);

            for (int i = 0; i < capacity; i++)
                Assert.That(zone.TryAdd(NewMinion()), Is.True, $"slot {i}");

            Assert.That(zone.IsFull, Is.True);
            Assert.That(zone.FreeSlots, Is.EqualTo(0));
            Assert.That(zone.TryAdd(NewMinion()), Is.False);
            Assert.That(zone.Count, Is.EqualTo(capacity));
        }

        [Test]
        public void Zone_AfterRemove_AcceptsAgain()
        {
            var field = _state.GetPlayer(0).Field;
            var first = NewMinion();
            Assert.That(field.TryAdd(first), Is.True);
            Assert.That(field.TryAdd(NewMinion()), Is.False);

            Assert.That(field.Remove(first), Is.True);
            Assert.That(field.TryAdd(NewMinion()), Is.True);
        }

        [Test]
        public void Zone_TryInsert_KeepsOrder()
        {
            var minions = _state.GetPlayer(0).Minions;
            var a = NewMinion();
            var b = NewMinion();
            var c = NewMinion();
            minions.TryAdd(a);
            minions.TryAdd(c);
            minions.TryInsert(b, 1);

            Assert.That(minions.Entities, Is.EqualTo(new[] { a, b, c }));
        }

        [Test]
        public void Zone_SameEntityTwice_Throws()
        {
            var minions = _state.GetPlayer(0).Minions;
            var a = NewMinion();
            minions.TryAdd(a);
            Assert.Throws<InvalidOperationException>(() => minions.TryAdd(a));
        }

        [Test]
        public void Zone_SlotCounts_ComeFromRuleConfig()
        {
            var rules = new RuleConfig { MinionSlots = 7 };
            var state = new GameState(rules, seed: 1);
            Assert.That(state.GetPlayer(1).Minions.Capacity, Is.EqualTo(7));
        }

        [Test]
        public void UnlimitedZone_NeverFull()
        {
            var deck = _state.GetPlayer(0).Deck;
            for (int i = 0; i < 100; i++)
                Assert.That(deck.TryAdd(NewMinion()), Is.True);
            Assert.That(deck.IsFull, Is.False);
        }
    }
}
