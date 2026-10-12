using System;
using System.Collections;
using System.Collections.Generic;
using TCG.Core.Data;
using TCG.Core.Events;

namespace TCG.Client.Presentation
{
    /// <summary>이벤트 1개의 연출. 코루틴으로 재생한다.</summary>
    public interface IEventPresenter
    {
        IEnumerator Play(PresentationContext context, GameEvent gameEvent);
    }

    /// <summary>함수 하나로 만든 연출.</summary>
    public sealed class FuncPresenter : IEventPresenter
    {
        readonly Func<PresentationContext, GameEvent, IEnumerator> _play;
        public FuncPresenter(Func<PresentationContext, GameEvent, IEnumerator> play) { _play = play; }
        public IEnumerator Play(PresentationContext context, GameEvent gameEvent) => _play(context, gameEvent);
    }

    /// <summary>
    /// (이벤트 종류, 연출 종류 ID) → 연출. 연출 종류는 카드·효과의 presentation 설정에서 온다 (GameDesign §7.3).
    /// 해당 종류가 없으면 "default"를 쓴다. 새 연출 = 새 항목 등록 (기존 코드 수정 없음).
    /// </summary>
    public sealed class PresenterRegistry
    {
        readonly Dictionary<(Type, string), IEventPresenter> _map = new Dictionary<(Type, string), IEventPresenter>();

        public void Register<T>(IEventPresenter presenter, string style = CardPresentation.Default) where T : GameEvent =>
            _map[(typeof(T), style ?? CardPresentation.Default)] = presenter;

        public void Register<T>(Func<PresentationContext, T, IEnumerator> play, string style = CardPresentation.Default) where T : GameEvent =>
            Register<T>(new FuncPresenter((c, e) => play(c, (T)e)), style);

        public IEventPresenter Find(Type eventType, string style)
        {
            if (style != null && _map.TryGetValue((eventType, style), out var presenter))
                return presenter;
            return _map.TryGetValue((eventType, CardPresentation.Default), out presenter) ? presenter : null;
        }

        public static PresenterRegistry CreateDefault()
        {
            var registry = new PresenterRegistry();
            DefaultPresenters.RegisterAll(registry);
            return registry;
        }
    }
}
