using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TCG.Core.Commands;
using TCG.Core.Data;
using TCG.Core.Events;
using TCG.Core.Rules;
using TCG.Core.State;

namespace TCG.Tests.Core
{
    /// <summary>
    /// 규칙 테스트용 대전 준비 도우미. 무작위 없이 원하는 상태를 직접 만든다.
    /// 플레이어 0의 턴, 양쪽 코스트 10으로 시작한다.
    /// </summary>
    sealed class TestGame
    {
        public GameState State { get; }
        public GameEngine Engine { get; } = new GameEngine();
        public List<GameEvent> LastEvents { get; private set; } = new List<GameEvent>();
        /// <summary>true면 선택 요청에 기본값으로 자동 응답한다 (제시된 순서 그대로 전부 발동, 대응은 패스).</summary>
        public bool AutoDecide { get; set; } = true;

        public TestGame(RuleConfig rules = null, int seed = 1)
        {
            State = new GameState(rules ?? RuleConfig.CreateDefault(), seed);
            State.Phase = GamePhase.InProgress;
            State.ActivePlayerIndex = 0;
            State.TurnNumber = 1;
            for (int p = 0; p < 2; p++)
            {
                var player = State.GetPlayer(p);
                player.MaxCost = 10;
                player.AvailableCost = 10;
                player.TurnsTaken = 5;
            }
        }

        public PlayerState P0 => State.GetPlayer(0);
        public PlayerState P1 => State.GetPlayer(1);

        public Entity AddToHand(int player, CardDefinition card) => Add(State.GetPlayer(player).Hand, player, card);
        public Entity AddToDeck(int player, CardDefinition card) => Add(State.GetPlayer(player).Deck, player, card);

        /// <summary>보드에 바로 놓는다 (공격 가능 상태, 등장 시 효과 없음).</summary>
        public Entity AddToBoard(int player, CardDefinition card)
        {
            var zone = State.GetPlayer(player).GetZone(RuleContext.BoardZoneFor(card.Type));
            return Add(zone, player, card);
        }

        Entity Add(Zone zone, int player, CardDefinition card)
        {
            var e = State.CreateEntity(player, card);
            Assert.That(zone.TryAdd(e), Is.True, $"{zone.Type} is full");
            return e;
        }

        public void FillDeck(int player, int count)
        {
            for (int i = 0; i < count; i++)
                AddToDeck(player, TestCards.Minion("c_filler", 1, 1, 1));
        }

        public ValidationResult Validate(Command command) => Engine.Validate(State, command);

        public List<GameEvent> Apply(Command command)
        {
            LastEvents = Engine.Apply(State, command).ToList();
            for (int guard = 0; AutoDecide && State.Decision != null; guard++)
            {
                Assert.That(guard, Is.LessThan(10000), "선택 요청이 끝나지 않음");
                LastEvents.AddRange(Engine.Apply(State, DefaultAnswer(State.Decision)));
            }
            return LastEvents;
        }

        public static Command DefaultAnswer(PendingDecision decision)
        {
            if (decision.Type == DecisionType.ChooseTriggers)
                return new ChooseTriggersCommand(decision.PlayerIndex, decision.Options.ToList());
            return new PassChainCommand(decision.PlayerIndex);
        }

        public List<GameEvent> Play(Entity card, int? target = null, int? position = null) =>
            Apply(new PlayCardCommand(card.OwnerIndex, card.Id, target, position));

        public List<GameEvent> Attack(Entity attacker, int targetId) =>
            Apply(new AttackCommand(attacker.OwnerIndex, attacker.Id, targetId));

        public List<GameEvent> EndTurn() => Apply(new EndTurnCommand(State.ActivePlayerIndex));

        public IEnumerable<T> Events<T>() where T : GameEvent => LastEvents.OfType<T>();
    }
}
