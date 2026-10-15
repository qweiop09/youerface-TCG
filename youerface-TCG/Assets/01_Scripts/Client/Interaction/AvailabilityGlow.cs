using System.Collections.Generic;
using TCG.Client.Battle;
using TCG.Client.Board;
using TCG.Client.Cards;
using TCG.Core.Commands;
using TCG.Core.State;
using UnityEngine;

namespace TCG.Client.Interaction
{
    /// <summary>
    /// 가능 여부 표시 (GameDesign §11.2): 내가 행동할 수 있을 때
    /// - 낼 수 있는 손패 카드: 초록 테두리
    /// - 공격할 수 있는 기물: 금색, 더 두꺼운 테두리
    /// - 체인 기회에 올릴 수 있는 효과의 카드: 하늘색, 가장 두꺼운 테두리
    /// 색만으로 구분하지 않도록 종류마다 테두리 두께가 다르다(색약 고려).
    /// 대상 지정·공격 조준 중(<see cref="BattleInput.IsIdle"/>가 아닐 때)에는 끄고 BattleInput의 대상 표시만 남긴다.
    /// </summary>
    public sealed class AvailabilityGlow : MonoBehaviour
    {
        public static readonly Color PlayableColor = new Color(0.35f, 1f, 0.45f);
        public static readonly Color AttackColor = new Color(1f, 0.78f, 0.22f);
        public static readonly Color ChainColor = new Color(0.4f, 0.8f, 1f);
        public const float PlayableWidth = 0.14f;
        public const float AttackWidth = 0.22f;
        public const float ChainWidth = 0.3f;

        /// <summary>상태가 바뀌지 않아도 이 간격(초)마다 다시 계산한다 (상태 객체를 그대로 고쳐 쓰는 경우 대비).</summary>
        const float RefreshSeconds = 0.25f;

        BattleSession _session;
        BattleInput _input;
        BoardView _board;

        readonly List<CardView> _glowing = new List<CardView>();
        float _nextRefresh;
        int _lastCommands = -1;
        bool _lastActive;

        public void Init(BattleSession session, BattleInput input, BoardView board)
        {
            _session = session;
            _input = input;
            _board = board;
        }

        void Update()
        {
            if (_session == null || _input == null || _board == null)
                return;

            bool active = _session.HumanCanAct && _input.IsIdle;
            bool changed = active != _lastActive || _session.CommandsApplied != _lastCommands;
            if (!changed && Time.unscaledTime < _nextRefresh)
                return;
            _lastActive = active;
            _lastCommands = _session.CommandsApplied;
            _nextRefresh = Time.unscaledTime + RefreshSeconds;

            Clear();
            if (active)
                Refresh();
        }

        void Refresh()
        {
            var state = _session.State;
            int human = _session.Human;

            // 체인 기회: 올릴 수 있는 효과의 카드만 표시한다 (다른 행동은 할 수 없음).
            if (state.Decision != null)
            {
                foreach (int triggerId in state.Decision.Options)
                {
                    var trigger = state.PendingTriggers.Find(t => t.Id == triggerId);
                    if (trigger != null)
                        Glow(trigger.SourceId, ChainColor, ChainWidth);
                }
                return;
            }

            var me = state.GetPlayer(human);
            var opponent = state.GetOpponent(human);
            var targets = Core.AI.LegalCommands.EffectTargets(state);

            foreach (var card in me.Hand.Entities)
            {
                if (CanPlay(card, targets))
                    Glow(card.Id, PlayableColor, PlayableWidth);
            }

            foreach (var minion in me.Minions.Entities)
            {
                if (_session.Validate(new AttackCommand(human, minion.Id, opponent.Hero.Id)).IsValid)
                {
                    Glow(minion.Id, AttackColor, AttackWidth);
                    continue;
                }
                foreach (var enemy in opponent.Minions.Entities)
                {
                    if (_session.Validate(new AttackCommand(human, minion.Id, enemy.Id)).IsValid)
                    {
                        Glow(minion.Id, AttackColor, AttackWidth);
                        break;
                    }
                }
            }
        }

        /// <summary>대상이 필요한 카드는 고를 수 있는 대상이 하나라도 있으면 낼 수 있다.</summary>
        bool CanPlay(Entity card, List<int> targets)
        {
            int human = _session.Human;
            if (!_session.RequiresTarget(card))
                return _session.Validate(new PlayCardCommand(human, card.Id)).IsValid;
            foreach (int target in targets)
            {
                if (_session.Validate(new PlayCardCommand(human, card.Id, target)).IsValid)
                    return true;
            }
            return false;
        }

        void Glow(int entityId, Color color, float width)
        {
            var view = _board.Card(entityId);
            if (view == null)
                return;
            view.SetGlow(color, width);
            _glowing.Add(view);
        }

        /// <summary>표시를 바로 끈다. BattleInput이 대상 표시를 켜기 전에 부른다 (같은 카드의 빛을 덮어쓰지 않게).</summary>
        public void Clear()
        {
            foreach (var view in _glowing)
            {
                if (view != null)
                    view.SetGlow(null);
            }
            _glowing.Clear();
        }
    }
}
