using System.Collections;
using System.Collections.Generic;
using TCG.Client.Cards;
using TCG.Core.Events;
using TCG.Core.State;
using UnityEngine;

namespace TCG.Client.Presentation
{
    /// <summary>
    /// 기본("default") 연출. GameDesign §7.3의 기본안:
    /// 카드 내기 = 크게 보여 준 뒤 칸으로 날아가 내려앉음 / 공격 = 대상에게 돌진 후 복귀 / 피해·회복 = 숫자 + 흔들림 /
    /// 사망·파괴 = 기울며 사라짐 / 효과 발동 = 노란 빛 + 체인 번호 / 효과 처리 = 흰 빛.
    /// 사운드는 유저 에셋(M4)이 붙으면 등장·퇴장 연출에서 재생한다.
    /// </summary>
    public static class DefaultPresenters
    {
        const float MoveTime = 0.3f;
        static readonly Color ActivationGlow = new Color(1f, 0.85f, 0.3f);
        static readonly Color ResolutionGlow = new Color(1f, 1f, 1f);
        static readonly Color DamageText = new Color(1f, 0.35f, 0.3f);
        static readonly Color HealText = new Color(0.45f, 1f, 0.5f);
        static readonly Color InfoText = new Color(1f, 0.85f, 0.45f);

        public static void RegisterAll(PresenterRegistry r)
        {
            r.Register<GameStartedEvent>((c, e) => c.Board.Hud.Banner("대전 시작", Color.white, 0.6f));
            r.Register<TurnStartedEvent>(TurnStarted);
            r.Register<CostChangedEvent>(CostChanged);
            r.Register<CardDrawnEvent>(Drawn);
            r.Register<CardBurnedEvent>(Burned);
            r.Register<CardDiscardedEvent>(Discarded);
            r.Register<CardPlayedEvent>(Played);
            r.Register<CardEnteredEvent>(Entered);
            r.Register<SpellCastEvent>(SpellCast);
            r.Register<EffectActivatedEvent>(Activated);
            r.Register<EffectResolvingEvent>(Resolving);
            r.Register<EffectNegatedEvent>(Negated);
            r.Register<ChainEndedEvent>((c, e) => ClearLingering(c));
            r.Register<DamageDealtEvent>(Damaged);
            r.Register<HealedEvent>(Healed);
            r.Register<StatsChangedEvent>(StatsChanged);
            r.Register<UsageChangedEvent>(UsageChanged);
            r.Register<CardExitedEvent>(Exited);
            r.Register<AttackEvent>(Attack);
            r.Register<FatigueEvent>((c, e) => Float(c, c.Board.HeroPosition(e.PlayerIndex), $"피로 {e.Damage}", InfoText));
            r.Register<PlacementFailedEvent>((c, e) => Float(c, c.Board.HeroPosition(e.PlayerIndex), "자리 없음", InfoText));
            r.Register<ResolutionLimitReachedEvent>((c, e) => c.Board.Hud.Banner("연쇄 한도 초과", InfoText, 1f));
            r.Register<GameEndedEvent>(GameEnded);
        }

        // ------------------------------------------------------------------ 턴

        static IEnumerator TurnStarted(PresentationContext c, TurnStartedEvent e)
        {
            bool mine = c.IsMine(e.PlayerIndex);
            c.Board.Hud.SetTurn(e.TurnNumber, mine);
            yield return c.Board.Hud.Banner(mine ? "내 차례" : "상대 차례", mine ? Color.white : new Color(1f, 0.7f, 0.5f), 0.55f);
        }

        static IEnumerator CostChanged(PresentationContext c, CostChangedEvent e)
        {
            c.Board.Hud.SetCost(c.Board.SideOf(e.PlayerIndex), e.AvailableCost, e.MaxCost);
            yield break;
        }

        static IEnumerator GameEnded(PresentationContext c, GameEndedEvent e)
        {
            string text = e.WinnerIndex == null ? "무승부" : c.IsMine(e.WinnerIndex.Value) ? "승리" : "패배";
            yield return c.Board.Hud.Banner(text, Color.white, 1.2f, stay: true);
        }

        // ------------------------------------------------------------------ 손패

        static IEnumerator Drawn(PresentationContext c, CardDrawnEvent e)
        {
            var entity = c.Snapshot(e.EntityId);
            var view = c.Board.EnsureCard(entity, c.IsMine(e.PlayerIndex), c.Board.DeckPosition(e.PlayerIndex), Quaternion.identity);
            c.Board.AddToHand(view);
            yield return c.Board.Layout(e.PlayerIndex, MoveTime);
        }

        static IEnumerator Burned(PresentationContext c, CardBurnedEvent e)
        {
            var view = c.Board.EnsureCard(c.Snapshot(e.EntityId), true, c.Board.DeckPosition(e.PlayerIndex), Quaternion.identity);
            c.Board.Detach(view);
            var raised = c.Board.DeckPosition(e.PlayerIndex) + new Vector3(-1.2f, 1.2f, 0);
            yield return Tween.Move(view.transform, raised, view.transform.localRotation, MoveTime, 0.3f);
            yield return Float(c, raised, "소멸", InfoText);
            yield return Vanish(c, view);
        }

        static IEnumerator Discarded(PresentationContext c, CardDiscardedEvent e)
        {
            var view = c.Board.Card(e.EntityId) ?? c.Board.EnsureCard(c.Snapshot(e.EntityId), true, c.Board.DeckPosition(e.PlayerIndex), Quaternion.identity);
            c.Board.Detach(view);
            view.SetFaceUp(true);
            var from = view.transform.localPosition;
            yield return Tween.All(c.Host, new[]
            {
                Tween.Move(view.transform, from + Vector3.up * 1.2f, view.transform.localRotation, MoveTime),
                c.Board.Layout(e.PlayerIndex, MoveTime),
            });
            yield return Vanish(c, view);
        }

        // ------------------------------------------------------------------ 카드 내기

        static IEnumerator Played(PresentationContext c, CardPlayedEvent e)
        {
            var view = c.Board.Card(e.EntityId) ?? c.Board.EnsureCard(c.Snapshot(e.EntityId), true, c.Board.HeroPosition(e.PlayerIndex), Quaternion.identity);
            c.Board.Detach(view);
            view.SetFaceUp(true);

            // 내가 낸 기물·필드·동료는 이미 아는 카드이므로 크게 띄우지 않고 바로 칸으로 간다 (등장 연출이 옮김).
            if (c.IsMine(e.PlayerIndex) && view.Type != Core.Data.CardType.Spell)
            {
                yield return c.Board.Layout(e.PlayerIndex, MoveTime * 0.5f);
                yield break;
            }

            if (!c.IsMine(e.PlayerIndex))
            {
                // 상대 카드: 화면 가운데에 크게 띄운 뒤(상세 카드), 실제 카드는 바로 자리로 (주문은 옆에 띄워 둠).
                c.Host.StartCoroutine(c.Board.Layout(e.PlayerIndex, MoveTime));
                yield return c.Board.Hud.BigCard.Show(c.Snapshot(e.EntityId), "상대가 낸 카드");
                if (view.Type == Core.Data.CardType.Spell)
                {
                    c.Board.ShowcasePose(out var side, out var sideRot, -3.2f);
                    yield return Tween.Move(view.transform, side, sideRot, MoveTime, 0f, Vector3.one * 1.1f);
                }
                // 상대가 고른 대상을 화살표로 잠깐 보여 준다 (GameDesign §11.2).
                if (e.TargetId.HasValue)
                    yield return PointAt(c, view.transform.position, e.TargetId.Value, OpponentTargetSeconds);
                yield break;
            }

            c.Board.ShowcasePose(out var pos, out var rot);
            yield return Tween.All(c.Host, new[]
            {
                Tween.Move(view.transform, pos, rot, MoveTime, 0f, Vector3.one * 1.7f),
                c.Board.Layout(e.PlayerIndex, MoveTime),
            });
            yield return Tween.Wait(0.15f);
        }

        static IEnumerator Entered(PresentationContext c, CardEnteredEvent e)
        {
            var view = c.Board.Card(e.EntityId);
            bool created = view == null;
            if (created)
            {
                view = c.Board.EnsureCard(c.Snapshot(e.EntityId), true, Vector3.zero, Quaternion.identity);
                view.transform.localScale = Vector3.one * 0.01f;
            }

            c.Board.Sounds?.PlayEnter(view.Definition);
            c.Board.InsertInZone(view, e.Zone, e.Index);
            view.Slot = e.Slot;
            c.Board.TargetPose(view, out var pos, out var rot);
            if (created)
                view.transform.localPosition = pos;

            yield return Tween.All(c.Host, new[]
            {
                Tween.Move(view.transform, pos, rot, MoveTime, created ? 0f : 0.8f, Vector3.one),
                c.Board.Layout(view.OwnerIndex, MoveTime, view),
            });
            yield return Tween.Punch(view.transform, 0.12f, 0.15f);
        }

        static IEnumerator SpellCast(PresentationContext c, SpellCastEvent e)
        {
            var view = c.Board.Card(e.EntityId);
            if (view == null)
                yield break;
            c.Board.Sounds?.PlayEnter(view.Definition);
            yield return Tween.Wait(0.25f);
            // 효과가 처리되는 동안 옆으로 비켜 작게 띄워 둔다 (발동 빛이 이 카드에 보이도록).
            c.Board.ShowcasePose(out var pos, out var rot, -3.2f);
            yield return Tween.Move(view.transform, pos, rot, MoveTime, 0f, Vector3.one * 1.1f);
            c.Lingering.Add(view);
        }

        // ------------------------------------------------------------------ 효과 (체인)

        static IEnumerator Activated(PresentationContext c, EffectActivatedEvent e)
        {
            var effect = c.Effect(e.SourceId, e.EffectIndex);
            if (effect?.Presentation != null && !effect.Presentation.Enabled)
                yield break;
            var view = c.Board.Card(e.SourceId);
            if (view == null)
                yield break;
            view.SetGlow(ActivationGlow);
            yield return Tween.All(c.Host, new[]
            {
                Tween.Punch(view.transform, 0.1f, 0.25f),
                Float(c, view.transform.position, $"체인 {e.ChainNumber}", ActivationGlow, 3.2f, 0.6f),
            });
        }

        static IEnumerator Resolving(PresentationContext c, EffectResolvingEvent e)
        {
            var view = c.Board.Card(e.SourceId);
            var effect = c.Effect(e.SourceId, e.EffectIndex);
            if (view == null)
                yield break;
            if (effect?.Presentation != null && !effect.Presentation.Enabled)
            {
                view.SetGlow(null);
                yield break;
            }
            view.SetGlow(ResolutionGlow);
            yield return Tween.Punch(view.transform, 0.15f, 0.22f);
            view.SetGlow(null);
        }

        static IEnumerator Negated(PresentationContext c, EffectNegatedEvent e)
        {
            var view = c.Board.Card(e.SourceId);
            if (view == null)
                yield break;
            view.SetGlow(null);
            yield return Float(c, view.transform.position, "무효", new Color(0.75f, 0.75f, 0.8f));
        }

        static IEnumerator ClearLingering(PresentationContext c)
        {
            var routines = new List<IEnumerator>();
            foreach (var view in c.Lingering)
                routines.Add(Vanish(c, view));
            c.Lingering.Clear();
            yield return Tween.All(c.Host, routines);
        }

        // ------------------------------------------------------------------ 수치

        static IEnumerator Damaged(PresentationContext c, DamageDealtEvent e)
        {
            var card = c.Board.Card(e.TargetId);
            var hero = card == null ? c.After.FindHero(e.TargetId) : null;
            Transform target;
            if (card != null)
            {
                card.AddHealth(-e.Amount);
                target = card.transform;
            }
            else if (hero != null)
            {
                var view = c.Board.HeroOf(hero.OwnerIndex);
                view.AddHealth(-e.Amount);
                target = view.transform;
            }
            else
            {
                yield break;
            }

            yield return Tween.All(c.Host, new[]
            {
                Tween.Shake(target, 0.12f, 0.25f),
                Float(c, target.position, $"-{e.Amount}", DamageText),
            });
        }

        static IEnumerator Healed(PresentationContext c, HealedEvent e)
        {
            var card = c.Board.Card(e.TargetId);
            var hero = card == null ? c.After.FindHero(e.TargetId) : null;
            Transform target;
            if (card != null)
            {
                card.AddHealth(e.Amount);
                target = card.transform;
            }
            else if (hero != null)
            {
                var view = c.Board.HeroOf(hero.OwnerIndex);
                view.AddHealth(e.Amount);
                target = view.transform;
            }
            else
            {
                yield break;
            }
            yield return Tween.All(c.Host, new[]
            {
                Tween.Punch(target, 0.1f, 0.25f),
                Float(c, target.position, $"+{e.Amount}", HealText),
            });
        }

        static IEnumerator StatsChanged(PresentationContext c, StatsChangedEvent e)
        {
            var view = c.Board.Card(e.EntityId);
            if (view == null)
                yield break;
            bool changed = view.Attack != e.Attack || view.Health != e.Health || view.MaxHealth != e.MaxHealth || view.Cost != e.Cost;
            view.SetStats(e.Attack, e.Health, e.MaxHealth, e.Cost);
            if (changed)
                yield return Tween.Punch(view.transform, 0.1f, 0.18f);
        }

        static IEnumerator UsageChanged(PresentationContext c, UsageChangedEvent e)
        {
            var view = c.Board.Card(e.EntityId);
            if (view == null)
                yield break;
            view.SetUsage(e.Usage);
            yield return Tween.Punch(view.transform, 0.1f, 0.18f);
        }

        // ------------------------------------------------------------------ 퇴장 / 공격

        static IEnumerator Exited(PresentationContext c, CardExitedEvent e)
        {
            var view = c.Board.Card(e.EntityId);
            if (view == null)
                yield break;
            c.Board.Sounds?.PlayExit(view.Definition);
            c.Board.Detach(view);
            view.SetGlow(null);
            int owner = view.OwnerIndex;

            var t = view.transform;
            var tilted = t.localRotation * Quaternion.Euler(0f, 0f, e.Reason == ExitReason.Replaced ? 0f : 25f);
            var to = t.localPosition + (e.Reason == ExitReason.Replaced ? new Vector3(-1.5f, 0.3f, 0f) : new Vector3(0f, -0.2f, 0f));
            yield return Tween.All(c.Host, new[]
            {
                Tween.Move(t, to, tilted, 0.4f, 0f, Vector3.one * 0.05f),
                Tween.Delayed(0.2f, c.Board.Layout(owner, MoveTime)),
            });
            c.Board.Destroy(view);
        }

        static IEnumerator Attack(PresentationContext c, AttackEvent e)
        {
            var attacker = c.Board.Card(e.AttackerId);
            var targetPos = c.Board.PositionOf(c.After, e.TargetId) ?? c.Board.PositionOf(c.Before, e.TargetId);
            if (attacker == null || targetPos == null)
                yield break;
            // 상대의 공격은 대상을 화살표로 먼저 보여 준다.
            if (!c.IsMine(attacker.OwnerIndex))
                yield return PointAt(c, attacker.transform.position, e.TargetId, OpponentAttackArrowSeconds);
            var t = attacker.transform;
            var home = t.localPosition;
            var homeRot = t.localRotation;
            var hit = Vector3.Lerp(home, t.parent.InverseTransformPoint(targetPos.Value), 0.75f) + Vector3.up * 0.3f;
            yield return Tween.Move(t, home + Vector3.up * 0.4f, homeRot, 0.1f);
            yield return Tween.Move(t, hit, homeRot, 0.14f);
            yield return Tween.Move(t, home, homeRot, 0.2f);
        }

        // ------------------------------------------------------------------ 도우미

        const float OpponentTargetSeconds = 0.8f;
        const float OpponentAttackArrowSeconds = 0.45f;

        /// <summary>from에서 대상(카드·영웅)까지 화살표를 seconds 동안 보여 준다 (연출 배속 적용).</summary>
        static IEnumerator PointAt(PresentationContext c, Vector3 from, int targetId, float seconds)
        {
            var to = c.Board.PositionOf(c.Before, targetId) ?? c.Board.PositionOf(c.After, targetId);
            if (to == null)
                yield break;
            var arrow = Interaction.TargetArrow.Create(c.Board.EffectRoot);
            arrow.Show(from, to.Value, true);
            yield return Tween.Wait(seconds);
            if (arrow != null)
                Object.Destroy(arrow.gameObject);
        }

        static IEnumerator Float(PresentationContext c, Vector3 worldPos, string text, Color color, float size = 5f, float duration = 0.8f) =>
            FloatingText.Play(c.Board.EffectRoot, c.Board.Camera, text, color, worldPos, size, duration);

        static IEnumerator Vanish(PresentationContext c, CardView view)
        {
            if (view == null)
                yield break;
            yield return Tween.Move(view.transform, view.transform.localPosition, view.transform.localRotation, 0.2f, 0f, Vector3.one * 0.01f);
            c.Board.Destroy(view);
        }

        /// <summary>묶음이 끝날 때 남은 떠 있는 카드를 치운다.</summary>
        public static IEnumerator Cleanup(PresentationContext c) => ClearLingering(c);
    }
}
