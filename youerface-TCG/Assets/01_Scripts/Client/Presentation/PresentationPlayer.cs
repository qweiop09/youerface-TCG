using System.Collections;
using System.Collections.Generic;
using TCG.Client.Board;
using TCG.Core.Data;
using TCG.Core.Effects;
using TCG.Core.Events;
using TCG.Core.State;
using UnityEngine;

namespace TCG.Client.Presentation
{
    /// <summary>
    /// 연출 재생기 (GameDesign §7.2·§7.3). 명령 1개의 이벤트 묶음을 차례로 재생한다.
    /// - 구조 이벤트(카드 내기, 체인 발동·처리, 공격, 턴 등)는 하나씩 순서대로.
    /// - 이어지는 결과 이벤트(피해, 사망, 수치 변화, 드로우 등)는 대상별 줄(track)로 나눠 병렬로.
    ///   직전 처리 효과의 피격 방식이 "하나씩"이면 줄마다 조금씩 늦게 시작한다.
    /// - 묶음이 끝나면 화면을 실제 상태와 맞추고, 어긋났던 수를 <see cref="TotalDrift"/>에 더한다.
    /// </summary>
    public sealed class PresentationPlayer : MonoBehaviour
    {
        sealed class Batch
        {
            public List<GameEvent> Events;
            public GameState Before;
            public GameState After;
        }

        public const float HitStagger = 0.28f;

        readonly Queue<Batch> _queue = new Queue<Batch>();
        BoardView _board;
        PresenterRegistry _presenters;
        EffectRegistry _effects;
        bool _running;

        public bool IsPlaying => _running || _queue.Count > 0;
        public int BatchesQueued { get; private set; }
        public int BatchesPlayed { get; private set; }
        public int TotalDrift { get; private set; }
        /// <summary>이벤트 하나의 연출을 시작할 때 (행동 기록·체인 표시가 연출 순서를 따라가게).</summary>
        public event System.Action<PresentationContext, GameEvent> EventStarting;

        public void Init(BoardView board, PresenterRegistry presenters = null, EffectRegistry effects = null)
        {
            _board = board;
            _presenters = presenters ?? PresenterRegistry.CreateDefault();
            _effects = effects ?? EffectRegistry.Default;
            EventStarting += board.Hud.Log.Record;
            EventStarting += board.Hud.Chain.Record;
        }

        /// <summary>before·after는 이후 바뀌지 않는 복사본을 넘긴다 (GameState.Clone).</summary>
        public void Enqueue(IReadOnlyList<GameEvent> events, GameState before, GameState after)
        {
            _queue.Enqueue(new Batch { Events = new List<GameEvent>(events), Before = before, After = after });
            BatchesQueued++;
            if (!_running)
                StartCoroutine(Run());
        }

        IEnumerator Run()
        {
            _running = true;
            while (_queue.Count > 0)
                yield return Tween.Safe(PlayBatch(_queue.Dequeue()));
            _running = false;
        }

        IEnumerator PlayBatch(Batch batch)
        {
            _board.RevealFrom(batch.Before);
            var context = new PresentationContext(_board, batch.Before, batch.After, _effects, this);
            var events = batch.Events;
            var hitMode = HitPresentationMode.Simultaneous;

            int i = 0;
            while (i < events.Count)
            {
                if (IsResult(events[i]))
                {
                    var tracks = new List<List<GameEvent>>();
                    var keys = new List<int>();
                    for (; i < events.Count && IsResult(events[i]); i++)
                    {
                        int key = TrackKey(context, events[i]);
                        int index = keys.IndexOf(key);
                        if (index < 0)
                        {
                            keys.Add(key);
                            tracks.Add(new List<GameEvent>());
                            index = tracks.Count - 1;
                        }
                        tracks[index].Add(events[i]);
                    }

                    // 여러 대상이 한 번에 맞으면(광역 피해) 화면 오른쪽 카드부터 차례로 (GameDesign §7.3).
                    tracks = OrderRightFirst(context, tracks);
                    // 여러 장이 맞으면 일정한 간격으로 차례로 (하나씩 차례로 설정이면 더 길게).
                    int damageTracks = tracks.FindAll(t => t[0] is DamageDealtEvent).Count;
                    float stagger = hitMode == HitPresentationMode.Sequential ? HitStagger : damageTracks > 1 ? DamageStagger : 0f;
                    var routines = new List<IEnumerator>();
                    for (int t = 0; t < tracks.Count; t++)
                        routines.Add(Tween.Delayed(stagger * t, PlayTrack(context, tracks[t])));
                    yield return Tween.All(this, routines);
                    continue;
                }

                var e = events[i++];
                hitMode = e is EffectResolvingEvent resolving
                    ? context.Effect(resolving.SourceId, resolving.EffectIndex)?.Presentation?.HitMode ?? HitPresentationMode.Simultaneous
                    : HitPresentationMode.Simultaneous;
                yield return Play(context, e);
            }

            yield return DefaultPresenters.Cleanup(context);
            yield return _board.SyncTo(batch.After, 0.2f);
            TotalDrift += _board.LastDriftCount;
            if (_board.LastDriftCount > 0)
                Debug.LogWarning($"[Presentation] 연출이 끝난 뒤 화면과 상태가 {_board.LastDriftCount}곳 달랐습니다 (자동으로 맞춤).");
            BatchesPlayed++;
        }

        /// <summary>피해 줄은 맞는 대상의 화면 X가 큰(오른쪽) 순서로, 그 밖의 줄은 원래 순서대로 뒤에.</summary>
        List<List<GameEvent>> OrderRightFirst(PresentationContext context, List<List<GameEvent>> tracks)
        {
            float X(List<GameEvent> track) =>
                track[0] is DamageDealtEvent d ? (_board.PositionOf(context.Before, d.TargetId) ?? _board.PositionOf(context.After, d.TargetId))?.x ?? 0f : 0f;
            bool IsDamage(List<GameEvent> track) => track[0] is DamageDealtEvent;
            // OrderBy는 같은 값이면 원래 순서를 지킨다.
            return System.Linq.Enumerable.ToList(System.Linq.Enumerable.ThenByDescending(
                System.Linq.Enumerable.OrderBy(tracks, t => IsDamage(t) ? 0 : 1), X));
        }

        /// <summary>여러 대상이 한 번에 맞을 때 오른쪽부터 맞는 간격 (초, 연출 배속 적용).</summary>
        const float DamageStagger = 0.2f;

        IEnumerator PlayTrack(PresentationContext context, List<GameEvent> track)
        {
            foreach (var e in track)
                yield return Play(context, e);
        }

        IEnumerator Play(PresentationContext context, GameEvent e)
        {
            EventStarting?.Invoke(context, e);
            var presenter = _presenters.Find(e.GetType(), StyleOf(context, e));
            return presenter == null ? null : Tween.Safe(presenter.Play(context, e));
        }

        /// <summary>카드·효과의 연출 설정에서 연출 종류 ID를 고른다.</summary>
        static string StyleOf(PresentationContext context, GameEvent e)
        {
            switch (e)
            {
                case CardEnteredEvent entered: return context.Definition(entered.EntityId)?.Presentation?.Enter;
                case CardExitedEvent exited: return context.Definition(exited.EntityId)?.Presentation?.Exit;
                case EffectActivatedEvent activated: return context.Effect(activated.SourceId, activated.EffectIndex)?.Presentation?.Activation;
                case EffectResolvingEvent resolving: return context.Effect(resolving.SourceId, resolving.EffectIndex)?.Presentation?.Resolution;
                default: return CardPresentation.Default;
            }
        }

        /// <summary>
        /// 효과·행동의 결과로 생기는 이벤트 (대상별로 병렬 재생).
        /// 전투 피해는 빼서 하나씩 순서대로 재생한다: 공격자의 타격 → 반격 (각각 타격 연출 + 소리가 끝날 때까지).
        /// </summary>
        static bool IsResult(GameEvent e) =>
            (e is DamageDealtEvent d && d.DamageType != TCG.Core.Data.DamageType.Combat) || e is HealedEvent || e is CardExitedEvent || e is StatsChangedEvent ||
            e is UsageChangedEvent || e is CardDrawnEvent || e is CardBurnedEvent || e is CardDiscardedEvent ||
            e is CardEnteredEvent || e is PlacementFailedEvent || e is FatigueEvent || e is CostChangedEvent;

        /// <summary>같은 대상의 이벤트는 한 줄에서 순서대로 (예: 피격 → 사망).</summary>
        static int TrackKey(PresentationContext context, GameEvent e)
        {
            switch (e)
            {
                case DamageDealtEvent d: return d.TargetId;
                case HealedEvent h: return h.TargetId;
                case CardExitedEvent x: return x.EntityId;
                case StatsChangedEvent s: return s.EntityId;
                case UsageChangedEvent u: return u.EntityId;
                case CardEnteredEvent n: return n.EntityId;
                case FatigueEvent f: return context.After.GetPlayer(f.PlayerIndex).Hero.Id;
                case CardDrawnEvent dr: return -1000 - dr.PlayerIndex;
                case CardBurnedEvent b: return -1000 - b.PlayerIndex;
                case CardDiscardedEvent ds: return -1000 - ds.PlayerIndex;
                case PlacementFailedEvent p: return -2000 - p.PlayerIndex;
                case CostChangedEvent c: return -3000 - c.PlayerIndex;
                default: return int.MinValue;
            }
        }
    }
}
