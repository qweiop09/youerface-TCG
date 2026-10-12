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
        /// bonusCard: 후공 보정 카드 (RuleConfig.SecondPlayerGetsBonusCard가 켜져 있을 때만). 어떤 카드인지는 미정.
        /// </summary>
        public IReadOnlyList<GameEvent> StartGame(GameState state, IReadOnlyList<CardDefinition> deck0,
            IReadOnlyList<CardDefinition> deck1, CardDefinition bonusCard = null)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));
            if (state.Phase != GamePhase.NotStarted)
                throw new InvalidOperationException("Game already started.");

            var ctx = new RuleContext(state, Registry);
            var decks = new[] { deck0 ?? Array.Empty<CardDefinition>(), deck1 ?? Array.Empty<CardDefinition>() };
            for (int p = 0; p < GameState.PlayerCount; p++)
            {
                var deck = state.GetPlayer(p).Deck;
                for (int i = 0; i < decks[p].Count; i++)
                    deck.TryAdd(state.CreateEntity(p, decks[p][i]));
            }
            for (int p = 0; p < GameState.PlayerCount; p++)
                state.GetPlayer(p).Deck.Shuffle(state.Rng);

            int first = state.Rng.Next(GameState.PlayerCount);
            int second = 1 - first;
            state.Phase = GamePhase.InProgress;
            state.ActivePlayerIndex = first;
            ctx.Emit(new GameStartedEvent(first));

            ctx.DrawCards(first, state.Rules.FirstPlayerStartingHand);
            ctx.DrawCards(second, state.Rules.SecondPlayerStartingHand);

            if (state.Rules.SecondPlayerGetsBonusCard && bonusCard != null)
            {
                var player = state.GetPlayer(second);
                var bonus = state.CreateEntity(second, bonusCard);
                if (player.Hand.TryAdd(bonus))
                    ctx.Emit(new CardDrawnEvent(second, bonus.Id));
                else
                    player.Exile.TryAdd(bonus);
            }

            ctx.ResolveAll();
            if (state.Phase == GamePhase.InProgress)
                StartTurn(ctx, first);
            return ctx.Events;
        }

        // ------------------------------------------------------------------ 검증

        public ValidationResult Validate(GameState state, Command command)
        {
            if (state == null || command == null)
                return ValidationResult.Fail("잘못된 요청입니다.");
            if (state.Phase != GamePhase.InProgress)
                return ValidationResult.Fail("대전이 진행 중이 아닙니다.");
            if (command.PlayerIndex != state.ActivePlayerIndex)
                return ValidationResult.Fail("내 턴이 아닙니다.");

            switch (command)
            {
                case PlayCardCommand play: return ValidatePlay(state, play);
                case AttackCommand attack: return ValidateAttack(state, attack);
                case EndTurnCommand _: return ValidationResult.Ok;
                default: return ValidationResult.Fail("알 수 없는 명령입니다.");
            }
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
                    if (cmd.Position.HasValue && (cmd.Position.Value < 0 || cmd.Position.Value > player.Minions.Count))
                        return ValidationResult.Fail("잘못된 위치입니다.");
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

        /// <summary>명령을 적용하고 일어난 일을 반환한다. 검증에 실패하면 예외.</summary>
        public IReadOnlyList<GameEvent> Apply(GameState state, Command command)
        {
            var validation = Validate(state, command);
            if (!validation.IsValid)
                throw new InvalidOperationException(validation.Reason);

            var ctx = new RuleContext(state, Registry);
            switch (command)
            {
                case PlayCardCommand play: ApplyPlay(ctx, play); break;
                case AttackCommand attack: ApplyAttack(ctx, attack); break;
                case EndTurnCommand _: ApplyEndTurn(ctx); break;
            }
            return ctx.Events;
        }

        /// <summary>
        /// 카드 내기 순서: 코스트 지불 → 손패에서 제거 → 내 보드 카드들의 "카드 사용 시" 발동 대기
        /// → 카드 배치(필드는 기존 필드 교체·퇴장 발동 먼저) 및 "등장 시" 발동 대기 → 처리.
        /// </summary>
        void ApplyPlay(RuleContext ctx, PlayCardCommand cmd)
        {
            var state = ctx.State;
            var player = state.GetPlayer(cmd.PlayerIndex);
            var card = player.Hand.FindById(cmd.CardId);

            player.AvailableCost -= card.Cost;
            ctx.Emit(new CostChangedEvent(player.Index, player.MaxCost, player.AvailableCost));
            player.Hand.Remove(card);
            ctx.Emit(new CardPlayedEvent(player.Index, card.Id));

            ctx.QueueBoardTrigger(player.Index, TriggerType.OnCardPlayed);

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
                    ctx.QueueCast(card, cmd.TargetId);
                    break;
            }

            ctx.ResolveAll();
            if (card.Type == CardType.Spell)
                ctx.Emit(new SpellFinishedEvent(card.Id, card.Location ?? ZoneType.Graveyard));
        }

        /// <summary>
        /// 공격 순서: "공격 시" 발동 처리 → (공격자·대상이 살아 있으면) 서로 공격력만큼 동시에 피해 → 처리.
        /// 영웅은 반격하지 않는다.
        /// </summary>
        void ApplyAttack(RuleContext ctx, AttackCommand cmd)
        {
            var state = ctx.State;
            var attacker = state.GetPlayer(cmd.PlayerIndex).Minions.FindById(cmd.AttackerId);
            attacker.AttacksThisTurn++;
            ctx.Emit(new AttackEvent(attacker.Id, cmd.TargetId));

            ctx.QueueTrigger(attacker, TriggerType.OnAttack);
            ctx.ResolveAll();
            if (state.Phase != GamePhase.InProgress || !attacker.IsOnBoard)
                return;

            bool targetIsHero = state.FindHero(cmd.TargetId) != null;
            var targetMinion = targetIsHero ? null : ctx.FindBoardEntity(cmd.TargetId);
            if (!targetIsHero && targetMinion == null)
                return;

            int attackerDamage = attacker.Attack ?? 0;
            int counterDamage = targetMinion?.Attack ?? 0;
            ctx.DealDamage(attacker.Id, cmd.TargetId, attackerDamage);
            if (targetMinion != null)
                ctx.DealDamage(targetMinion.Id, attacker.Id, counterDamage);
            ctx.ResolveAll();
        }

        void ApplyEndTurn(RuleContext ctx)
        {
            var state = ctx.State;
            int current = state.ActivePlayerIndex;
            ctx.QueueBoardTrigger(current, TriggerType.OnTurnEnd);
            ctx.ResolveAll();
            if (state.Phase != GamePhase.InProgress)
                return;

            ctx.Emit(new TurnEndedEvent(current));
            StartTurn(ctx, 1 - current);
        }

        /// <summary>턴 시작: 최대 코스트 증가·충전 → 기물 공격 가능 상태로 → 1장 드로우 → "턴 시작 시" 발동.</summary>
        void StartTurn(RuleContext ctx, int playerIndex)
        {
            var state = ctx.State;
            var rules = state.Rules;
            var player = state.GetPlayer(playerIndex);

            state.ActivePlayerIndex = playerIndex;
            state.TurnNumber++;

            int nextMax = player.TurnsTaken == 0 ? rules.StartingMaxCost : player.MaxCost + rules.MaxCostGainPerTurn;
            player.MaxCost = Math.Min(nextMax, rules.MaxCostLimit);
            player.AvailableCost = player.MaxCost;
            player.TurnsTaken++;

            var minions = player.Minions.Entities;
            for (int i = 0; i < minions.Count; i++)
            {
                minions[i].PlayedThisTurn = false;
                minions[i].AttacksThisTurn = 0;
            }

            ctx.Emit(new TurnStartedEvent(playerIndex, state.TurnNumber));
            ctx.Emit(new CostChangedEvent(playerIndex, player.MaxCost, player.AvailableCost));

            ctx.DrawCard(playerIndex);
            ctx.QueueBoardTrigger(playerIndex, TriggerType.OnTurnStart);
            ctx.ResolveAll();
        }
    }
}
