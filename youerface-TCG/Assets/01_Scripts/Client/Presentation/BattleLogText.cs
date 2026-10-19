using TCG.Client.Common;
using TCG.Core.Data;
using TCG.Core.Events;
using TCG.Core.Net;
using TCG.Core.State;

namespace TCG.Client.Presentation
{
    /// <summary>행동 기록 한 줄. Side = 누가 한 일인가 (0 = 나, 1 = 상대, -1 = 누구도 아님). Separator = 턴·대전 구분 줄.</summary>
    public readonly struct BattleLogLine
    {
        public readonly string Text;
        public readonly int Side;
        public readonly bool Separator;

        public BattleLogLine(string text, int side, bool separator = false)
        {
            Text = text;
            Side = side;
            Separator = separator;
        }
    }

    /// <summary>
    /// 이벤트 1개 → 행동 기록 한 줄 (GameDesign §11.3). 보는 사람 기준(나/상대)으로 쓰고, 가려진 카드는 이름을 쓰지 않는다.
    /// 코스트·수치·사용 횟수 변화처럼 다른 화면이 이미 보여 주는 이벤트는 기록하지 않는다(null).
    /// </summary>
    public static class BattleLogText
    {
        public static BattleLogLine? Describe(PresentationContext c, GameEvent e)
        {
            switch (e)
            {
                case GameStartedEvent _:
                    // 선공은 코인 토스가 끝난 뒤 FirstPlayer로 따로 쓴다 (동전보다 먼저 결과를 보이지 않게).
                    return new BattleLogLine("대전 시작", -1, true);
                case TurnStartedEvent turn:
                    return new BattleLogLine($"턴 {turn.TurnNumber} · {(c.IsMine(turn.PlayerIndex) ? "내 차례" : "상대 차례")}", -1, true);
                case CardDrawnEvent drawn:
                    // 상대 손패는 싱글 대전(시야 필터 없음)에서도 이름을 쓰지 않는다.
                    return Line(c, drawn.PlayerIndex, !c.IsMine(drawn.PlayerIndex) || IsHidden(c, drawn.EntityId)
                        ? "카드 1장 뽑음" : $"카드 뽑음: {Bold(CardName(c, drawn.EntityId))}");
                case CardBurnedEvent burned:
                    return Line(c, burned.PlayerIndex, $"손패가 가득 차 {Bold(CardName(c, burned.EntityId))} 소멸");
                case FatigueEvent fatigue:
                    return Line(c, fatigue.PlayerIndex, $"덱이 비어 피로 피해 {fatigue.Damage}");
                case CardPlayedEvent played:
                {
                    var definition = c.Definition(played.EntityId);
                    string verb = definition?.Type == CardType.Spell ? "사용" : "냄";
                    string type = definition == null ? "" : KoreanTerms.Of(definition.Type) + " ";
                    string target = played.TargetId.HasValue ? $" → {Target(c, played.TargetId.Value)}" : "";
                    return Line(c, played.PlayerIndex, $"{type}{Bold(CardName(c, played.EntityId))} {verb}{target}");
                }
                case CardEnteredEvent entered:
                    // 손패에서 낸 카드는 "냄" 줄로 충분하다. 이번에 새로 생긴 카드(소환물)만 기록.
                    if (c.Before.GetEntity(entered.EntityId) != null)
                        return null;
                    return Line(c, c.OwnerOf(entered.EntityId), $"{KoreanTerms.Of(entered.Zone == ZoneType.Summon ? CardType.Summon : c.Definition(entered.EntityId)?.Type ?? CardType.Summon)} {Bold(CardName(c, entered.EntityId))} 등장");
                case CardExitedEvent exited:
                    return Line(c, c.OwnerOf(exited.EntityId), $"{Bold(CardName(c, exited.EntityId))} {ExitText(exited.Reason)}");
                case CardDiscardedEvent discarded:
                    return Line(c, discarded.PlayerIndex, $"{Bold(CardName(c, discarded.EntityId))} 버림");
                case PlacementFailedEvent failed:
                    return Line(c, failed.PlayerIndex, $"{ZoneName(failed.Zone)} 자리가 없어 놓지 못함");
                case EffectActivatedEvent activated:
                    return Line(c, activated.ControllerIndex,
                        $"체인 {activated.ChainNumber} 발동: {Bold(CardName(c, activated.SourceId))} ({KoreanTerms.Of(activated.Action)})");
                case EffectResolvingEvent resolving:
                    return Line(c, c.OwnerOf(resolving.SourceId), $"체인 {resolving.ChainNumber} 처리: {Bold(CardName(c, resolving.SourceId))}");
                case EffectNegatedEvent negated:
                    return Line(c, c.OwnerOf(negated.SourceId), $"체인 {negated.ChainNumber} 무효: {Bold(CardName(c, negated.SourceId))}");
                case AttackEvent attack:
                    return Line(c, c.OwnerOf(attack.AttackerId), $"{Bold(CardName(c, attack.AttackerId))} → {Target(c, attack.TargetId)} 공격");
                case DamageDealtEvent damage:
                    return new BattleLogLine($"{Target(c, damage.TargetId)} {damage.Amount} 피해 ({DamageName(damage.DamageType)})", -1);
                case HealedEvent healed:
                    return new BattleLogLine($"{Target(c, healed.TargetId)} {healed.Amount} 회복", -1);
                case ResolutionLimitReachedEvent _:
                    return new BattleLogLine("연쇄 한도 초과 — 남은 효과를 버림", -1);
                case GameEndedEvent ended:
                    return new BattleLogLine(ended.WinnerIndex == null ? "무승부" : c.IsMine(ended.WinnerIndex.Value) ? "승리" : "패배", -1, true);
                default:
                    return null;
            }
        }

        /// <summary>코인 토스 결과 줄.</summary>
        public static BattleLogLine FirstPlayer(PresentationContext c, int firstPlayer) =>
            new BattleLogLine(c.IsMine(firstPlayer) ? "코인 토스: 내가 선공" : "코인 토스: 상대가 선공", c.IsMine(firstPlayer) ? 0 : 1);

        static BattleLogLine Line(PresentationContext c, int player, string text)
        {
            int side = player < 0 ? -1 : c.IsMine(player) ? 0 : 1;
            string who = side < 0 ? "" : side == 0 ? "나: " : "상대: ";
            return new BattleLogLine(who + text, side);
        }

        static Entity Find(PresentationContext c, int id) => c.After.GetEntity(id) ?? c.Before.GetEntity(id);

        static bool IsHidden(PresentationContext c, int id)
        {
            var entity = Find(c, id);
            return entity == null || HiddenCard.Is(entity);
        }

        static bool IsHero(PresentationContext c, int id, out int owner)
        {
            for (int p = 0; p < GameState.PlayerCount; p++)
            {
                if (c.After.GetPlayer(p).Hero.Id == id)
                {
                    owner = p;
                    return true;
                }
            }
            owner = -1;
            return false;
        }

        static string CardName(PresentationContext c, int id)
        {
            if (IsHero(c, id, out int owner))
                return c.IsMine(owner) ? "내 영웅" : "상대 영웅";
            var entity = Find(c, id);
            return entity == null || HiddenCard.Is(entity) ? "카드" : entity.Definition.Name;
        }

        /// <summary>대상 이름: 영웅이면 "내/상대 영웅", 카드면 주인을 붙인다.</summary>
        static string Target(PresentationContext c, int id)
        {
            if (IsHero(c, id, out int owner))
                return Bold(c.IsMine(owner) ? "내 영웅" : "상대 영웅");
            int cardOwner = c.OwnerOf(id);
            string prefix = cardOwner < 0 ? "" : c.IsMine(cardOwner) ? "내 " : "상대 ";
            return prefix + Bold(CardName(c, id));
        }

        static string Bold(string text) => "<b>" + text + "</b>";

        static string ExitText(ExitReason reason)
        {
            switch (reason)
            {
                case ExitReason.Died: return "사망";
                case ExitReason.Destroyed: return "파괴됨";
                case ExitReason.Replaced: return "새 필드로 교체됨";
                case ExitReason.UsageDepleted: return "사용 횟수를 다 써서 사라짐";
                default: return "퇴장";
            }
        }

        static string DamageName(DamageType type)
        {
            switch (type)
            {
                case DamageType.Combat: return "전투";
                case DamageType.Fatigue: return "피로";
                default: return "효과";
            }
        }

        static string ZoneName(ZoneType zone)
        {
            switch (zone)
            {
                case ZoneType.Minion: return "기물";
                case ZoneType.Field: return "필드";
                case ZoneType.Companion: return "동료";
                case ZoneType.Summon: return "소환물";
                case ZoneType.Hand: return "손패";
                default: return zone.ToString();
            }
        }
    }
}
