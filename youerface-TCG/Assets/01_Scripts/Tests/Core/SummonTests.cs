using System.Linq;
using NUnit.Framework;
using TCG.Core.Data;
using TCG.Core.Events;

namespace TCG.Tests.Core
{
    public class SummonTests
    {
        static CardDefinition EmberFor(SummonDuplicateMode mode, int usage = 2) => TestCards.Ember(usage, mode);

        [Test]
        public void CreateSummon_PlacesSummonWithUsage()
        {
            var g = new TestGame();
            g.Play(g.AddToHand(0, TestCards.SummonSpellFor(EmberFor(SummonDuplicateMode.AddNew))));

            var summon = g.P0.Summons.Entities.Single();
            Assert.That(summon.Usage, Is.EqualTo(2));
            Assert.That(summon.Type, Is.EqualTo(CardType.Summon));
        }

        [Test]
        public void AddNew_PlacesAnotherCopy()
        {
            var g = new TestGame();
            var def = TestCards.SummonSpellFor(EmberFor(SummonDuplicateMode.AddNew));
            g.Play(g.AddToHand(0, def));
            g.Play(g.AddToHand(0, def));

            Assert.That(g.P0.Summons.Count, Is.EqualTo(2));
        }

        [Test]
        public void RefreshUsage_RefillsExistingInsteadOfAdding()
        {
            var g = new TestGame();
            var def = TestCards.SummonSpellFor(EmberFor(SummonDuplicateMode.RefreshUsage, usage: 3));
            g.Play(g.AddToHand(0, def));
            var summon = g.P0.Summons.Entities.Single();
            summon.Usage = 1;

            g.Play(g.AddToHand(0, def));

            Assert.That(g.P0.Summons.Count, Is.EqualTo(1));
            Assert.That(summon.Usage, Is.EqualTo(3));
            Assert.That(g.Events<UsageChangedEvent>().Single().Usage, Is.EqualTo(3));
        }

        [Test]
        public void RefreshUsage_TreatsDifferentCardsSummonsAsDifferent()
        {
            var g = new TestGame();
            g.Play(g.AddToHand(0, TestCards.SummonSpellFor(EmberFor(SummonDuplicateMode.RefreshUsage), "c_a")));
            g.Play(g.AddToHand(0, TestCards.SummonSpellFor(EmberFor(SummonDuplicateMode.RefreshUsage), "c_b")));

            Assert.That(g.P0.Summons.Count, Is.EqualTo(2));
        }

        [Test]
        public void FullSummonZone_NewSummonIsIgnored()
        {
            var g = new TestGame();
            var def = TestCards.SummonSpellFor(EmberFor(SummonDuplicateMode.AddNew));
            for (int i = 0; i < 4; i++)
                g.Play(g.AddToHand(0, def));

            g.Play(g.AddToHand(0, def));

            Assert.That(g.P0.Summons.Count, Is.EqualTo(4));
            Assert.That(g.Events<PlacementFailedEvent>().Count(), Is.EqualTo(1));
        }

        [Test]
        public void FullSummonZone_RefreshStillWorks()
        {
            var g = new TestGame();
            var refresh = TestCards.SummonSpellFor(EmberFor(SummonDuplicateMode.RefreshUsage, usage: 3), "c_refresh");
            g.Play(g.AddToHand(0, refresh));
            var target = g.P0.Summons.Entities.Single();
            for (int i = 0; i < 3; i++)
                g.Play(g.AddToHand(0, TestCards.SummonSpellFor(EmberFor(SummonDuplicateMode.AddNew), "c_other")));
            target.Usage = 1;

            g.Play(g.AddToHand(0, refresh));

            Assert.That(g.P0.Summons.IsFull, Is.True);
            Assert.That(target.Usage, Is.EqualTo(3));
            Assert.That(g.Events<PlacementFailedEvent>(), Is.Empty);
        }

        [Test]
        public void Summon_TriggersAtTurnEnd_AndIsRemovedAtZeroUsage()
        {
            var g = new TestGame();
            g.FillDeck(0, 5);
            g.FillDeck(1, 5);
            g.Play(g.AddToHand(0, TestCards.SummonSpellFor(EmberFor(SummonDuplicateMode.AddNew, usage: 2))));
            var summon = g.P0.Summons.Entities.Single();

            g.EndTurn(); // 플레이어 0 턴 종료: 상대 영웅 1 피해, 사용 횟수 1
            Assert.That(g.P1.Hero.Health, Is.EqualTo(29));
            Assert.That(summon.Usage, Is.EqualTo(1));

            g.EndTurn(); // 플레이어 1 턴 종료: 발동 없음
            Assert.That(summon.Usage, Is.EqualTo(1));

            g.EndTurn(); // 플레이어 0 턴 종료: 다시 발동 후 제거
            Assert.That(g.P1.Hero.Health, Is.EqualTo(28));
            Assert.That(g.P0.Summons.Count, Is.EqualTo(0));
            Assert.That(g.P0.Graveyard.Contains(summon), Is.False, "소환물은 묘지로 가지 않는다");
        }
    }
}
