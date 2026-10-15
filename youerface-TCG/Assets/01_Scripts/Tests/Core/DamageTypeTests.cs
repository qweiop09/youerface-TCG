using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using TCG.Core.AI;
using TCG.Core.Commands;
using TCG.Core.Data;
using TCG.Core.Events;
using TCG.Core.Net;
using TCG.Core.Scoring;
using TCG.Core.Serialization;
using TCG.Core.State;

namespace TCG.Tests.Core
{
    /// <summary>M5.5-0: 피해 유형 분리(GameDesign §7.6), 피해 트리거 6종, 카드 설명(flavor), 스키마 v3, 기물 5칸.</summary>
    public class DamageTypeTests
    {
        /// <summary>트리거마다 공격력 +bonus를 주는 표시용 효과 (자신 대상).</summary>
        static EffectDefinition Mark(TriggerType trigger, int bonus) =>
            TestCards.Effect(trigger, TargetType.Self, ActionType.ModifyAttack, bonus);

        /// <summary>트리거 6종에 각각 다른 자리수 표시를 단 기물. 공격력 증가량으로 어느 트리거가 걸렸는지 읽는다.</summary>
        static CardDefinition Watcher(string id, int attack, int health)
        {
            var def = TestCards.Minion(id, 1, attack, health);
            def.Effects.Add(Mark(TriggerType.OnEffectDamageTaken, 1));
            def.Effects.Add(Mark(TriggerType.OnCombatDamageTaken, 10));
            def.Effects.Add(Mark(TriggerType.OnAnyDamageTaken, 100));
            def.Effects.Add(Mark(TriggerType.OnEffectDamageDealt, 1000));
            def.Effects.Add(Mark(TriggerType.OnCombatDamageDealt, 10000));
            def.Effects.Add(Mark(TriggerType.OnAnyDamageDealt, 100000));
            return def;
        }

        [Test]
        public void EffectDamage_TriggersEffectAndAny_OnBothSides()
        {
            var g = new TestGame();
            var target = g.AddToBoard(1, Watcher("c_target", 0, 10));
            var shooterDef = Watcher("c_shooter", 0, 10);
            shooterDef.Effects.Add(TestCards.Effect(TriggerType.OnEnter, TargetType.Chosen, ActionType.Damage, 1));
            var shooter = g.AddToHand(0, shooterDef);

            g.Play(shooter, target: target.Id);

            var hit = g.Events<DamageDealtEvent>().Single();
            Assert.That(hit.DamageType, Is.EqualTo(DamageType.Effect));
            Assert.That(target.Attack, Is.EqualTo(101), "받는 쪽: 효과 + 모든");
            Assert.That(shooter.Attack, Is.EqualTo(101000), "주는 쪽: 효과 + 모든");
        }

        [Test]
        public void CombatDamage_IncludesCounter_TriggersCombatAndAny()
        {
            var g = new TestGame();
            var attacker = g.AddToBoard(0, Watcher("c_atk", 1, 50));
            var defender = g.AddToBoard(1, Watcher("c_def", 1, 50));

            g.Attack(attacker, defender.Id);

            var hits = g.Events<DamageDealtEvent>().ToList();
            Assert.That(hits.Count, Is.EqualTo(2));
            Assert.That(hits.All(h => h.DamageType == DamageType.Combat), Is.True);
            // 1(기본 공격력) + 받을 때(전투 10 + 모든 100) + 줄 때(전투 10000 + 모든 100000)
            Assert.That(attacker.Attack, Is.EqualTo(1 + 110110));
            Assert.That(defender.Attack, Is.EqualTo(1 + 110110), "반격도 전투 피해");
        }

        [Test]
        public void AttackingHero_TriggersOnlyDealtSide()
        {
            var g = new TestGame();
            var attacker = g.AddToBoard(0, Watcher("c_atk", 2, 5));

            g.Attack(attacker, g.P1.Hero.Id);

            Assert.That(g.P1.Hero.Health, Is.EqualTo(28));
            Assert.That(attacker.Attack, Is.EqualTo(2 + 110000));
        }

        [Test]
        public void CounterAttacker_KilledInCombat_StillTriggersDealt()
        {
            var g = new TestGame();
            var attacker = g.AddToBoard(0, TestCards.Minion("c_big", 1, 5, 10));
            var defDef = TestCards.Minion("c_spiky", 1, 1, 1);
            defDef.Effects.Add(TestCards.Effect(TriggerType.OnCombatDamageDealt, TargetType.EnemyHero, ActionType.Damage, 3));
            var defender = g.AddToBoard(1, defDef);

            g.Attack(attacker, defender.Id);

            Assert.That(g.P1.Graveyard.Contains(defender), Is.True);
            Assert.That(attacker.Health, Is.EqualTo(9), "반격 1");
            Assert.That(g.P0.Hero.Health, Is.EqualTo(27), "쓰러진 반격 기물의 \"전투 피해를 줄 때\"");
        }

        [Test]
        public void Fatigue_IsFatigueType_WithNoSource()
        {
            var g = new TestGame();
            g.EndTurn(); // 상대 덱이 비어 있으므로 상대 턴 시작 드로우에서 피로 피해

            var hit = g.Events<DamageDealtEvent>().Single();
            Assert.That(hit.DamageType, Is.EqualTo(DamageType.Fatigue));
            Assert.That(hit.SourceId, Is.EqualTo(0));
            Assert.That(hit.TargetId, Is.EqualTo(g.P1.Hero.Id));
        }

        [Test]
        public void SpellDamage_DoesNotTriggerCombatOnTarget()
        {
            var g = new TestGame();
            var def = TestCards.Minion("c_only_combat", 1, 0, 10);
            def.Effects.Add(Mark(TriggerType.OnCombatDamageTaken, 1));
            var target = g.AddToBoard(1, def);

            g.Play(g.AddToHand(0, TestCards.Spell("c_bolt", 0,
                TestCards.Effect(TriggerType.OnEnter, TargetType.Chosen, ActionType.Damage, 2))), target: target.Id);

            Assert.That(target.Health, Is.EqualTo(8));
            Assert.That(target.Attack, Is.EqualTo(0));
        }

        [Test]
        public void DamageTriggers_ConsumeUsage()
        {
            var g = new TestGame();
            var companion = g.AddToBoard(0, TestCards.CompanionWith("c_pinger", 2,
                TestCards.Effect(TriggerType.OnTurnEnd, TargetType.EnemyHero, ActionType.Damage, 1),
                TestCards.Effect(TriggerType.OnEffectDamageDealt, TargetType.EnemyHero, ActionType.Damage, 1)));
            g.FillDeck(1, 2);

            g.EndTurn();

            // 턴 종료 1(사용 2→1) → 줄 때 1(1→0, 퇴장). 두 번째 피해 때 이미 모인 "줄 때"는
            // 기존 규칙대로 주인이 퇴장해도 처리된다 → 피해 3, 그 뒤로는 보드에 없어 더 모이지 않음.
            Assert.That(g.P0.Graveyard.Contains(companion), Is.True);
            Assert.That(g.P1.Hero.Health, Is.EqualTo(27));
        }

        // ------------------------------------------------------------------ 스키마 v3 / flavor

        [Test]
        public void MigrateV2_OnDamagedBecomesAnyDamageTaken_IncludingNestedSummon()
        {
            const string json = @"{ ""schemaVersion"": 2, ""type"": ""Minion"", ""cost"": 1, ""attack"": 1, ""health"": 1,
                ""effects"": [
                    { ""trigger"": { ""type"": ""OnDamaged"" }, ""target"": { ""type"": ""Self"" }, ""action"": { ""type"": ""ModifyAttack"", ""amount"": 1 } },
                    { ""trigger"": { ""type"": ""OnEnter"" }, ""target"": { ""type"": ""Self"" },
                      ""action"": { ""type"": ""CreateSummon"", ""summon"": { ""type"": ""Summon"", ""usage"": 1, ""onDuplicate"": ""AddNew"",
                          ""effects"": [ { ""trigger"": { ""type"": ""OnDamaged"" }, ""target"": { ""type"": ""EnemyHero"" },
                                           ""action"": { ""type"": ""Damage"", ""amount"": 1 } } ] } } } ] }";

            var card = CardSerializer.Deserialize(json);

            Assert.That(card.Effects[0].Trigger.Type, Is.EqualTo(TriggerType.OnAnyDamageTaken));
            Assert.That(card.Effects[1].Trigger.Type, Is.EqualTo(TriggerType.OnEnter));
            Assert.That(card.Effects[1].Action.Summon.Effects[0].Trigger.Type, Is.EqualTo(TriggerType.OnAnyDamageTaken));
            Assert.That(card.Flavor, Is.Null);
            Assert.That(JObject.Parse(CardSerializer.Serialize(card))["schemaVersion"].Value<int>(), Is.EqualTo(3));
        }

        [Test]
        public void MigrateV1_AlsoRunsV2Step()
        {
            const string json = @"{ ""schemaVersion"": 1, ""type"": ""Minion"", ""cost"": 1, ""attack"": 1, ""health"": 1, ""presentationStyle"": ""fire"",
                ""effects"": [ { ""trigger"": { ""type"": ""OnDamaged"" }, ""target"": { ""type"": ""Self"" }, ""action"": { ""type"": ""ModifyAttack"", ""amount"": 1 } } ] }";

            var card = CardSerializer.Deserialize(json);

            Assert.That(card.Presentation.Enter, Is.EqualTo("fire"));
            Assert.That(card.Effects[0].Trigger.Type, Is.EqualTo(TriggerType.OnAnyDamageTaken));
        }

        [Test]
        public void Flavor_RoundTrips_AndIsOmittedWhenEmpty()
        {
            var card = TestCards.FireSpirit();
            Assert.That(JObject.Parse(CardSerializer.Serialize(card))["flavor"], Is.Null);

            card.Flavor = "불꽃 속에서 태어났다.\n두 번째 줄";
            var restored = CardSerializer.Deserialize(CardSerializer.Serialize(card));
            Assert.That(restored.Flavor, Is.EqualTo(card.Flavor));
        }

        [Test]
        public void Flavor_DoesNotChangeScore()
        {
            var table = ScoreTable.CreateDefault();
            var card = TestCards.FireSpirit();
            int before = ScoreCalculator.Calculate(card, table);
            card.Flavor = new string('가', 80);
            Assert.That(ScoreCalculator.Calculate(card, table), Is.EqualTo(before));
        }

        [Test]
        public void Flavor_LengthIsChecked_FromRuleConfig_IncludingSummon()
        {
            var rules = RuleConfig.CreateDefault();
            var card = TestCards.FireSpirit();
            card.Flavor = new string('가', rules.MaxFlavorLength);
            Assert.That(CardValidator.Validate(card, null, rules), Is.Empty);

            card.Flavor += "!";
            Assert.That(CardValidator.Validate(card, null, rules).Count, Is.EqualTo(1));
            Assert.That(CardValidator.Validate(card, null, new RuleConfig { MaxFlavorLength = 200 }), Is.Empty);

            var summon = TestCards.Ember();
            summon.Flavor = new string('나', rules.MaxFlavorLength + 1);
            Assert.That(CardValidator.Validate(TestCards.SummonSpell(summon), null, rules).Count, Is.EqualTo(1));
        }

        // ------------------------------------------------------------------ 점수표

        [TestCase(TriggerType.OnEffectDamageTaken)]
        [TestCase(TriggerType.OnCombatDamageTaken)]
        [TestCase(TriggerType.OnAnyDamageTaken)]
        [TestCase(TriggerType.OnEffectDamageDealt)]
        [TestCase(TriggerType.OnCombatDamageDealt)]
        [TestCase(TriggerType.OnAnyDamageDealt)]
        public void DamageTriggers_UseOldDamagedMultiplier_AndRepeatWithUsage(TriggerType trigger)
        {
            var table = ScoreTable.CreateDefault();
            Assert.That(table.Version, Is.EqualTo(2));
            Assert.That(table.TriggerMultiplier(trigger), Is.EqualTo(120));
            Assert.That(table.RepeatingTriggers, Does.Contain(trigger));

            // 기물 1/1 1코: 기본 -10 + 스탯 20 - 코스트 20 = -10, 적 영웅 피해 2 → 20 × 1.2 = 24
            var card = TestCards.Minion("c_s", 1, 1, 1);
            card.Effects.Add(TestCards.Effect(trigger, TargetType.EnemyHero, ActionType.Damage, 2));
            Assert.That(ScoreCalculator.Calculate(card, table), Is.EqualTo(-10 + 24));
        }

        // ------------------------------------------------------------------ 네트워크

        [Test]
        public void DamageEvent_DamageType_SurvivesEventCodec()
        {
            var e = new DamageDealtEvent(3, 4, 5, DamageType.Combat);
            var json = EventCodec.Encode(e);
            Assert.That((string)json["damageType"], Is.EqualTo("Combat"));

            var back = (DamageDealtEvent)EventCodec.Decode(JObject.Parse(json.ToString()));
            Assert.That(back.DamageType, Is.EqualTo(DamageType.Combat));
            Assert.That(back.Amount, Is.EqualTo(5));
        }

        [Test]
        public void ProtocolVersion_WasBumped_ForDamageType()
        {
            Assert.That(NetProtocol.Version, Is.GreaterThanOrEqualTo(2));
        }

        // ------------------------------------------------------------------ 5칸 / AI

        [Test]
        public void FiveMinions_CanBePlaced_SixthCannot()
        {
            var g = new TestGame();
            Assert.That(g.State.Rules.MinionSlots, Is.EqualTo(5));
            for (int i = 0; i < 5; i++)
                g.Play(g.AddToHand(0, TestCards.Minion("c_m", 0, 1, 1)));
            Assert.That(g.P0.Minions.Count, Is.EqualTo(5));
            Assert.That(g.Validate(new PlayCardCommand(0, g.AddToHand(0, TestCards.Minion("c_m", 0, 1, 1)).Id)).IsValid, Is.False);
        }

        [TestCase(AiDifficulty.Normal)]
        [TestCase(AiDifficulty.Hard)]
        public void Ai_FillsFiveSlots(AiDifficulty difficulty)
        {
            var g = new TestGame();
            g.FillDeck(0, 3);
            g.FillDeck(1, 3);
            for (int i = 0; i < 6; i++)
                g.AddToHand(0, TestCards.Minion("c_cheap", 1, 2, 2));
            var ai = AiPlayers.Create(difficulty, 7, g.Engine);

            for (int guard = 0; guard < 50 && g.State.ActivePlayerIndex == 0 && g.State.Phase == GamePhase.InProgress; guard++)
            {
                var command = ai.ChooseCommand(g.State, 0);
                Assert.That(g.Validate(command).IsValid, Is.True);
                g.Apply(command);
            }
            Assert.That(g.P0.Minions.Count, Is.EqualTo(5));
        }
    }
}
