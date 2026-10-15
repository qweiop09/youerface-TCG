using System;
using System.Collections.Generic;
using TCG.Core.Commands;
using TCG.Core.Data;
using TCG.Core.Effects;
using TCG.Core.Events;
using TCG.Core.State;

namespace TCG.Core.Rules
{
    /// <summary>
    /// 게임 규칙 엔진. 상태 변경은 오직 여기를 통해서만 한다.
    /// 같은 시드 + 같은 덱 + 같은 명령 목록이면 항상 같은 상태가 된다(결정론).
    /// </summary>
    public sealed class GameEngine
    {
        public EffectRegistry Registry { get; }

        public GameEngine(EffectRegistry registry = null)
        {
            Registry = registry ?? EffectRegistry.Default;
        }

        // ------------------------------------------------------------------ 대전 시작

        /// <summary>
        /// 덱을 만들어 섞고, 선공을 정하고, 시작 손패를 뽑은 뒤 선공의 첫 턴을 시작한다.
        /// bonusCard: 후공 보정 카드 (RuleConfig.SecondPlayerGetsBonusCard가 켜져 있을 때만, 기본은 꺼짐 — Q14 보정 카드 없음).
        /// firstPlayer: 0/1이면 그 자리가 선공 (온라인 코인 토스, <see cref="Net.CoinToss"/>). -1이면 시드 난수로 정한다.
        /// 어느 쪽이든 난수 1번을 쓰므로 같은 시드면 덱 섞기·드로우는 같다.
        /// </summary>
        public IReadOnlyList<GameEvent> StartGame(GameState state, IReadOnlyList<CardDefinition> deck0,
            IReadOnlyList<CardDefinition> deck1, CardDefinition bonusCard = null, int firstPlayer = -1)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));
            if (state.Phase != GamePhase.NotStarted)
                throw new InvalidOperationException("Game already started.");
            if (firstPlayer < -1 || firstPlayer >= GameState.PlayerCount)
                throw new ArgumentOutOfRangeException(nameof(firstPlayer));

            var ctx = new RuleContext(state, Registry);
            var decks = new[] { deck0 ?? Array.Empty<CardDefinition>(), deck1 ?? Array.Empty<CardDefinition>() };
            // 섞은 순서대로 카드를 만든다: 카드 ID가 덱 목록 순서를 드러내지 않게 (온라인 시야 필터, Architecture §6).
            // 섞기 결과(덱 순서)와 난수 사용량은 "만든 뒤 섞기"와 같다.
            for (int p = 0; p < GameState.PlayerCount; p++)
            {
                var order = new List<CardDefinition>(decks[p]);
                state.Rng.Shuffle(order);
                var deck = state.GetPlayer(p).Deck;
                for (int i = 0; i < order.Count; i++)
                    deck.TryAdd(state.CreateEntity(p, order[i]));
            }

            int rolled = state.Rng.Next(GameState.PlayerCount);
            int first = firstPlayer >= 0 ? firstPlayer : rolled;
            int second = 1 - first;
            state.Phase = GamePhase.InProgress;
            state.ActivePlayerIndex = first;
            ctx.Emit(new GameStartedEvent(first));

            ctx.DrawCards(first, state.Rules.FirstPlayerStartingHand);
            ctx.DrawCards(second, state.Rules.SecondPlayerStartingHand);

            // 후공 보정: 후공은 대전 시작부터 최대 코스트를 가지고 있다 (첫 턴에 늘어나는 것이 아님, GameDesign §11.3).
            var secondPlayer = state.GetPlayer(second);
            secondPlayer.MaxCost = Math.Min(state.Rules.SecondPlayerStartingMaxCost, state.Rules.MaxCostLimit);
            secondPlayer.AvailableCost = secondPlayer.MaxCost;
            ctx.Emit(new CostChangedEvent(second, secondPlayer.MaxCost, secondPlayer.AvailableCost));

            if (state.Rules.SecondPlayerGetsBonusCard && bonusCard != null)
            {
                var player = state.GetPlayer(second);
                var bonus = state.CreateEntity(second, bonusCard);
                if (player.Hand.TryAdd(bonus))
                    ctx.Emit(new CardDrawnEvent(second, bonus.Id));
                else
                    player.Exile.TryAdd(bonus);
            }

            ctx.Settle();
            if (state.Phase == GamePhase.InProgress)
            {
                ctx.StartTurn(first);
                ctx.Advance();
            }
            return ctx.Events;
        }

        // ------------------------------------------------------------------ 검증

        public ValidationResult Validate(GameState state, Command command)
        {
            if (state == null || command == null)
                return ValidationResult.Fail("잘못된 요청입니다.");
            if (state.Phase != GamePhase.InProgress)
                return ValidationResult.Fail("대전이 진행 중이 아닙니다.");

            if (command is ConcedeCommand)
                return command.PlayerIndex >= 0 && command.PlayerIndex < GameState.PlayerCount
                    ? ValidationResult.Ok
                    : ValidationResult.Fail("잘못된 플레이어입니다.");

            if (state.Decision != null)
                return ValidateDecision(state, command);

            if (command.PlayerIndex != state.ActivePlayerIndex)
                return ValidationResult.Fail("내 턴이 아닙니다.");

            switch (command)
            {
                case PlayCardCommand play: return ValidatePlay(state, play);
                case AttackCommand attack: return ValidateAttack(state, attack);
                case EndTurnCommand _: return ValidationResult.Ok;
                case ChainActivateCommand _:
                case PassChainCommand _:
                    return ValidationResult.Fail("선택할 것이 없습니다.");
                default: return ValidationResult.Fail("알 수 없는 명령입니다.");
            }
        }

        /// <summary>선택 대기 중에는 그 선택에 대한 답만 받는다.</summary>
        static ValidationResult ValidateDecision(GameState state, Command command)
        {
            var decision = state.Decision;
            if (command.PlayerIndex != decision.PlayerIndex)
                return ValidationResult.Fail("상대의 선택을 기다리는 중입니다.");

            switch (command)
            {
                case ChainActivateCommand activate:
                    return Contains(decision.Options, activate.TriggerId)
                        ? ValidationResult.Ok
                        : ValidationResult.Fail("체인에 올릴 수 없는 효과입니다.");
                case PassChainCommand _:
                    return decision.CanPass
                        ? ValidationResult.Ok
                        : ValidationResult.Fail("강제 효과를 먼저 체인에 올려야 합니다.");
                default:
                    return ValidationResult.Fail("먼저 체인 선택을 끝내야 합니다.");
            }
        }

        static bool Contains(IReadOnlyList<int> list, int value)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == value)
                    return true;
            }
            return false;
        }

        ValidationResult ValidatePlay(GameState state, PlayCardCommand cmd)
        {
            var player = state.GetPlayer(cmd.PlayerIndex);
            var card = player.Hand.FindById(cmd.CardId);
            if (card == null)
                return ValidationResult.Fail("손패에 없는 카드입니다.");
            if (card.Cost > player.AvailableCost)
                return ValidationResult.Fail("코스트가 부족합니다.");

            switch (card.Type)
            {
                case CardType.Minion:
                    if (player.Minions.IsFull)
                        return ValidationResult.Fail("기물 칸이 가득 찼습니다.");
                    // 위치 = 칸 번호 (GameDesign §4.1). 비어 있는 칸이어야 한다. 없으면 가운데에 가까운 빈 칸.
                    if (cmd.Position.HasValue && !player.Minions.IsSlotFree(cmd.Position.Value))
                        return ValidationResult.Fail("그 칸에는 놓을 수 없습니다.");
                    break;
                case CardType.Companion:
                    if (player.Companions.IsFull)
                        return ValidationResult.Fail("동료 칸이 가득 찼습니다.");
                    break;
                case CardType.Field:
                case CardType.Spell:
                    break;
                default:
                    return ValidationResult.Fail("낼 수 없는 카드입니다.");
            }

            if (cmd.TargetId.HasValue)
            {
                if (!IsValidEffectTarget(state, cmd.TargetId.Value))
                    return ValidationResult.Fail("잘못된 대상입니다.");
            }
            else if (RequiresChosenTarget(card))
            {
                return ValidationResult.Fail("대상을 골라야 합니다.");
            }

            return ValidationResult.Ok;
        }

        /// <summary>카드의 "등장 시" 효과 중 플레이어가 대상을 골라야 하는 것이 있는가.</summary>
        public bool RequiresChosenTarget(Entity card)
        {
            var effects = card.Definition.Effects;
            if (effects == null)
                return false;
            for (int i = 0; i < effects.Count; i++)
            {
                var e = effects[i];
                if (e?.Trigger == null || e.Trigger.Type != TriggerType.OnEnter || e.Target == null || e.Action == null)
                    continue;
                if (Registry.TryGetAction(e.Action.Type, out var action) && action.UsesTargets
                    && Registry.TryGetTarget(e.Target.Type, out var selector) && selector.RequiresChoice)
                    return true;
            }
            return false;
        }

        static bool IsValidEffectTarget(GameState state, int id)
        {
            if (state.FindHero(id) != null)
                return true;
            var e = state.FindEntity(id);
            return e != null && e.IsOnBoard;
        }

        ValidationResult ValidateAttack(GameState state, AttackCommand cmd)
        {
            var rules = state.Rules;
            var player = state.GetPlayer(cmd.PlayerIndex);
            var attacker = player.Minions.FindById(cmd.AttackerId);
            if (attacker == null)
                return ValidationResult.Fail("내 기물만 공격할 수 있습니다.");
            if ((attacker.Attack ?? 0) <= 0)
                return ValidationResult.Fail("공격력이 0입니다.");
            if (attacker.PlayedThisTurn && !rules.CanAttackOnPlayedTurn)
                return ValidationResult.Fail("이번 턴에 낸 기물은 공격할 수 없습니다.");
            if (attacker.AttacksThisTurn >= rules.AttacksPerTurn)
                return ValidationResult.Fail("이번 턴에 이미 공격했습니다.");

            var opponent = state.GetOpponent(cmd.PlayerIndex);
            bool targetIsHero = opponent.Hero.Id == cmd.TargetId;
            var targetMinion = targetIsHero ? null : opponent.Minions.FindById(cmd.TargetId);
            if (!targetIsHero && targetMinion == null)
                return ValidationResult.Fail("상대 기물 또는 영웅만 공격할 수 있습니다.");
            if (targetMinion != null && targetMinion.HasKeyword(Keyword.Stealth))
                return ValidationResult.Fail("은신 기물은 공격할 수 없습니다.");

            if (HasAttackableTaunt(opponent) && (targetMinion == null || !targetMinion.HasKeyword(Keyword.Taunt)))
                return ValidationResult.Fail("도발 기물부터 공격해야 합니다.");

            return ValidationResult.Ok;
        }

        /// <summary>공격 가능한 도발 기물이 있는가. 은신 도발은 공격할 수 없으므로 제외.</summary>
        static bool HasAttackableTaunt(PlayerState player)
        {
            var minions = player.Minions.Entities;
            for (int i = 0; i < minions.Count; i++)
            {
                if (minions[i].HasKeyword(Keyword.Taunt) && !minions[i].HasKeyword(Keyword.Stealth))
                    return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ 적용

        /// <summary>
        /// 명령을 적용하고 일어난 일을 반환한다. 검증에 실패하면 예외.
        /// 처리 도중 플레이어 선택이 필요하면 거기서 멈추고, <see cref="GameState.Decision"/>에 무엇을 기다리는지 남긴다.
        /// </summary>
        public IReadOnlyList<GameEvent> Apply(GameState state, Command command)
        {
            var validation = Validate(state, command);
            if (!validation.IsValid)
                throw new InvalidOperationException(validation.Reason);

            var ctx = new RuleContext(state, Registry);
            if (command is ConcedeCommand)
            {
                ctx.Concede(command.PlayerIndex);
                return ctx.Events;
            }
            switch (command)
            {
                case PlayCardCommand play:
                    state.ResolutionCount = 0;
                    ApplyPlay(ctx, play);
                    break;
                case AttackCommand attack:
                    state.ResolutionCount = 0;
                    ApplyAttack(ctx, attack);
                    break;
                case EndTurnCommand _:
                    state.ResolutionCount = 0;
                    ctx.CollectBoardTriggers(state.ActivePlayerIndex, TriggerType.OnTurnEnd);
                    state.PendingActions.Add(new PendingAction(PendingActionType.FinishEndTurn, state.ActivePlayerIndex));
                    break;
                case ChainActivateCommand activate:
                    ctx.ActivateFromDecision(activate.TriggerId);
                    break;
                case PassChainCommand _:
                    ctx.PassChain();
                    break;
            }

            ctx.Advance();
            return ctx.Events;
        }

        /// <summary>
        /// 카드 내기: 코스트 지불 → 손패에서 제거 → 즉시 배치(소환 연출) / 필드는 기존 필드 퇴장 먼저 / 주문은 바로 묘지(일회성은 소멸).
        /// 이 행동으로 발동한 효과(내 보드의 "카드 사용 시", 낸 카드의 "등장 시", 교체된 필드의 "퇴장 시")는 체인으로 처리된다.
        /// </summary>
        void ApplyPlay(RuleContext ctx, PlayCardCommand cmd)
        {
            var state = ctx.State;
            var player = state.GetPlayer(cmd.PlayerIndex);
            var card = player.Hand.FindById(cmd.CardId);

            player.AvailableCost -= card.Cost;
            ctx.Emit(new CostChangedEvent(player.Index, player.MaxCost, player.AvailableCost));
            player.Hand.Remove(card);
            ctx.Emit(new CardPlayedEvent(player.Index, card.Id, cmd.TargetId));

            ctx.CollectBoardTriggers(player.Index, TriggerType.OnCardPlayed);

            switch (card.Type)
            {
                case CardType.Minion:
                    card.PlayedThisTurn = true;
                    ctx.TryPlaceOnBoard(card, cmd.Position, cmd.TargetId);
                    break;
                case CardType.Companion:
                    ctx.TryPlaceOnBoard(card, null, cmd.TargetId);
                    break;
                case CardType.Field:
                    if (player.Field.Count > 0)
                        ctx.RemoveFromBoard(player.Field.Entities[0], ExitReason.Replaced);
                    ctx.TryPlaceOnBoard(card, null, cmd.TargetId);
                    break;
                case CardType.Spell:
                    var destination = card.HasKeyword(Keyword.Exhaust) ? player.Exile : player.Graveyard;
                    destination.TryAdd(card);
                    ctx.Emit(new SpellCastEvent(card.Id, destination.Type));
                    ctx.CollectTriggers(card, TriggerType.OnEnter, cmd.TargetId, isCast: true);
                    break;
            }
        }

        /// <summary>공격: 공격 선언 → "공격 시" 체인 → (체인이 끝난 뒤) 전투 피해.</summary>
        void ApplyAttack(RuleContext ctx, AttackCommand cmd)
        {
            var state = ctx.State;
            var attacker = state.GetPlayer(cmd.PlayerIndex).Minions.FindById(cmd.AttackerId);
            attacker.AttacksThisTurn++;
            ctx.Emit(new AttackEvent(attacker.Id, cmd.TargetId));

            ctx.CollectTriggers(attacker, TriggerType.OnAttack);
            state.PendingActions.Add(new PendingAction(PendingActionType.CombatDamage, attacker.Id, cmd.TargetId));
        }
    }
}
