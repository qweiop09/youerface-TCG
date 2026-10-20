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
            r.Register<GameStartedEvent>(GameStarted);
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

        static IEnumerator GameStarted(PresentationContext c, GameStartedEvent e)
        {
            yield return c.Board.Hud.CoinToss.Play(c.IsMine(e.FirstPlayerIndex), c.After.Rules);
            c.Board.Hud.Log.FlushDeferred(c, e.FirstPlayerIndex);
        }

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
            yield return ToPile(c, view, e.EntityId);
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
            yield return ToPile(c, view, e.EntityId);
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
            // 사용한 주문은 묘지로, 일회성은 제외로 (실제 상태가 간 곳).
            foreach (var view in c.Lingering)
                routines.Add(view != null ? ToPile(c, view, view.EntityId) : Vanish(c, view));
            c.Lingering.Clear();
            yield return Tween.All(c.Host, routines);
        }

        // ------------------------------------------------------------------ 수치

        static IEnumerator Damaged(PresentationContext c, DamageDealtEvent e)
        {
            if (e.DamageType == Core.Data.DamageType.Combat)
                return CombatHit(c, e);
            return ShowDamage(c, e, pacedSoundAt: -1f);
        }

        // ------------------------------------------------------------------ 전투 (타격)

        /// <summary>소리 사이·연출 사이의 자연스러운 틈 (실제 시간).</summary>
        const float BeatGap = 0.15f;

        /// <summary>
        /// 전투 피해 1번 = 타격 1번: 때리는 카드가 공격 연출(돌진: 날아가 부딪힘 / 원거리: 대상에 이펙트) → 닿는 순간 피해 숫자·흔들림·파티클.
        /// 공격음은 타격 시작에, 피격음은 공격음이 끝난 뒤에 내고, 피격음이 끝나고 조금 쉰 뒤 다음 연출로 넘어간다.
        /// 공격자의 타격 다음에 반격이 같은 방식으로 이어진다 (사망은 전투가 끝난 뒤 — Core가 두 피해 뒤에 처리).
        /// </summary>
        static IEnumerator CombatHit(PresentationContext c, DamageDealtEvent e)
        {
            var striker = c.Board.Card(e.SourceId);
            var targetPos = c.Board.PositionOf(c.Before, e.TargetId) ?? c.Board.PositionOf(c.After, e.TargetId);
            if (striker == null || targetPos == null)
            {
                yield return ShowDamage(c, e, pacedSoundAt: Time.unscaledTime);
                yield break;
            }

            float attackLength = c.Board.Sounds?.PlayAttackNow(striker.Definition) ?? 0f;
            float attackEnds = Time.unscaledTime + attackLength;
            bool ranged = striker.Definition.Presentation?.Attack == Core.Data.CardPresentation.AttackRanged;
            var t = striker.transform;
            var home = t.localPosition;
            var homeRot = t.localRotation;
            var host = c.Host;

            if (ranged)
            {
                // 원거리: 제자리에서 기를 모으고(빛·튕김) 대상에 이펙트만.
                HitEffects.Burst(host, c.Board.EffectRoot, t.position, HitEffects.MagicColor, 0.5f, 10);
                yield return Tween.Punch(t, 0.12f, 0.18f);
                HitEffects.Burst(host, c.Board.EffectRoot, targetPos.Value, HitEffects.MagicColor, 1.3f, 28);
                yield return ShowDamage(c, e, pacedSoundAt: attackEnds + (attackLength > 0f ? BeatGap : 0f));
            }
            else
            {
                // 돌진: 들어 올린 뒤 대상에게 날아가 부딪히고(파티클) 돌아온다.
                var hit = Vector3.Lerp(home, t.parent.InverseTransformPoint(targetPos.Value), 0.8f) + Vector3.up * 0.3f;
                yield return Tween.Move(t, home + Vector3.up * 0.5f, homeRot, 0.12f);
                yield return Tween.Move(t, hit, homeRot, 0.12f);
                HitEffects.Burst(host, c.Board.EffectRoot, targetPos.Value, HitEffects.ImpactColor);
                yield return Tween.All(host, new[]
                {
                    Tween.Move(t, home, homeRot, 0.22f),
                    ShowDamage(c, e, pacedSoundAt: attackEnds + (attackLength > 0f ? BeatGap : 0f)),
                });
            }
        }

        /// <summary>
        /// 피해 숫자·흔들림·체력 감소. pacedSoundAt &gt;= 0이면(전투) 피격음을 그 시각(실제 시간)에 내고, 피격음이 끝나고 조금 쉴 때까지 기다린다.
        /// 아니면(효과 피해) 피격음을 차례 대기열에 넣는다.
        /// </summary>
        static IEnumerator ShowDamage(PresentationContext c, DamageDealtEvent e, float pacedSoundAt)
        {
            var card = c.Board.Card(e.TargetId);
            var hero = card == null ? c.After.FindHero(e.TargetId) : null;
            Transform target;
            bool paced = pacedSoundAt >= 0f;
            if (card != null)
            {
                card.AddHealth(-e.Amount);
                if (!paced)
                    c.Board.Sounds?.PlayDamaged(card.Definition); // 효과 피해: 대기열 (3개까지 겹침)
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
                paced ? PacedDamagedSound(c, card, pacedSoundAt) : null,
            });
        }

        /// <summary>공격음이 끝난 뒤 피격음을 내고, 끝나고 조금 쉴 때까지 기다린다 (소리가 끝나면 다음 연출).</summary>
        static IEnumerator PacedDamagedSound(PresentationContext c, CardView card, float at)
        {
            while (Time.unscaledTime < at)
                yield return null;
            float length = card != null ? c.Board.Sounds?.PlayDamagedNow(card.Definition) ?? 0f : 0f;
            float until = Time.unscaledTime + length + (length > 0f ? BeatGap : 0f);
            while (Time.unscaledTime < until)
                yield return null;
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
            // 피격 연출·피격음 바로 뒤에 겹치지 않게 잠깐 틈을 둔다.
            if (e.Reason == ExitReason.Died)
                yield return Tween.Wait(DeathPause);
            c.Board.Sounds?.PlayExit(view.Definition);
            c.Board.Detach(view);
            view.SetGlow(null);
            view.ShowStatus(false);
            int owner = view.OwnerIndex;

            // 기울며 쓰러진 뒤 묘지로 (소환물은 묘지에 가지 않고 그 자리에서 사라진다).
            var t = view.transform;
            var tilted = t.localRotation * Quaternion.Euler(0f, 0f, e.Reason == ExitReason.Replaced ? 0f : 25f);
            var to = t.localPosition + (e.Reason == ExitReason.Replaced ? new Vector3(-1.5f, 0.3f, 0f) : new Vector3(0f, 0.15f, 0f));
            yield return Tween.All(c.Host, new[]
            {
                Tween.Move(t, to, tilted, 0.25f),
                Tween.Delayed(0.2f, c.Board.Layout(owner, MoveTime)),
            });
            yield return ToPile(c, view, e.EntityId);
        }

        const float DeathPause = 0.25f;
        const float ToPileTime = 0.35f;

        /// <summary>
        /// 카드를 실제 상태에서 간 곳(묘지·제외)의 더미로 날려 보내고 더미 맨 위를 바꾼다. 묘지·제외가 아니면(소환물 등) 사라진다.
        /// </summary>
        static IEnumerator ToPile(PresentationContext c, CardView view, int entityId)
        {
            if (view == null)
                yield break;
            var entity = c.After.GetEntity(entityId);
            var zone = entity?.Location;
            if (zone != ZoneType.Graveyard && zone != ZoneType.Exile)
            {
                yield return Vanish(c, view);
                yield break;
            }
            var pile = c.Board.GraveOf(entity.OwnerIndex, zone.Value);
            view.SetFaceUp(true);
            yield return Tween.Move(view.transform, pile.transform.localPosition + Vector3.up * 0.06f, Quaternion.identity, ToPileTime, 0f, Vector3.one);
            var list = c.After.GetPlayer(entity.OwnerIndex).GetZone(zone.Value);
            pile.Refresh(entity, Mathf.Max(1, list.IndexOf(entity) + 1));
            c.Board.Destroy(view);
        }

        /// <summary>공격 선언: 상대의 공격은 대상을 화살표로 먼저 보여 준다. 실제 타격은 전투 피해 연출(<see cref="CombatHit"/>)이 한다.</summary>
        static IEnumerator Attack(PresentationContext c, AttackEvent e)
        {
            var attacker = c.Board.Card(e.AttackerId);
            if (attacker == null)
                yield break;
            if (!c.IsMine(attacker.OwnerIndex))
                yield return PointAt(c, attacker.transform.position, e.TargetId, OpponentAttackArrowSeconds);
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
