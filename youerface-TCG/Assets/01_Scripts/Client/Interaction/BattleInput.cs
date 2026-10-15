using System.Collections.Generic;
using TCG.Client.Battle;
using TCG.Client.Board;
using TCG.Client.Cards;
using TCG.Core.Commands;
using TCG.Core.Data;
using TCG.Core.State;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace TCG.Client.Interaction
{
    /// <summary>
    /// 마우스·터치 입력 (Input System의 Pointer).
    /// - 손패 카드 누르기 → 끌기 (연출 중에도 집을 수는 있다). 보드 쪽에 놓으면 내기, 손패 쪽이면 되돌림.
    /// - 대상이 필요한 카드는 놓은 뒤 화살표로 대상을 클릭 (오른쪽 클릭·Esc = 취소).
    /// - 내 기물 누르고 끌기 → 공격 화살표, 대상 위에서 놓으면 공격.
    /// - 연출 중이거나 내 차례가 아니면 내기·대상 선택·공격은 안 된다 (GameDesign §7.3).
    /// - 카드 클릭(끌지 않고 뗌) → 카드 확인 (앞면이 보이는 보드·손패 카드, 상대 손패 제외).
    /// 고를 수 있는 대상은 초록빛으로 표시한다.
    /// </summary>
    public sealed class BattleInput : MonoBehaviour
    {
        enum Mode { Idle, Pressing, Dragging, Targeting, Aiming }

        static readonly Color ValidGlow = new Color(0.35f, 1f, 0.45f);
        const float HoldHeight = 1.6f;
        /// <summary>
        /// 클릭과 끌기 구분: 이 거리(px)를 넘게 움직이면 끌기(손패 들기·공격 화살표).
        /// 손패 카드는 이 시간(초)을 넘게 눌러도 들어 올린다. 그 전에 떼면 카드 확인.
        /// </summary>
        const float DragPixels = 28f;
        const float HoldSeconds = 0.45f;

        BattleSession _session;
        BoardView _board;
        TargetArrow _arrow;
        AttackPreview _attackPreview;
        Mode _mode;
        CardView _held;
        CardView _pressed;
        Vector2 _pressPos;
        float _pressTime;
        bool _dragMoved;
        readonly List<CardView> _glowing = new List<CardView>();
        readonly List<int> _validTargets = new List<int>();
        /// <summary>마우스를 올려 튀어나와 있는 내 손패 카드 (Held로 배치에서 빼 둔다).</summary>
        CardView _hovered;
        /// <summary>끌고 있는 카드 크기 (손패보다 크게, GameDesign §11.2).</summary>
        const float DragScale = 1.5f;

        /// <summary>
        /// ESC 우선순위 (GameDesign §11.2): Esc·오른쪽 클릭은 진행 중인 동작(끌기·대상 지정·공격 조준, 카드 확인 창)을 먼저 취소한다.
        /// 취소한 프레임을 남겨, 나중에 붙는 ESC 메뉴(M5.5-2)는 <see cref="CancelConsumedThisFrame"/>가 false일 때만 열린다.
        /// </summary>
        public int CancelConsumedFrame { get; private set; } = -1;

        /// <summary>이번 프레임의 Esc를 취소에 썼는가 (BattleInput 또는 카드 확인 창).</summary>
        public bool CancelConsumedThisFrame =>
            CancelConsumedFrame == Time.frameCount || (_board != null && _board.Hud.Inspect.ClosedFrame == Time.frameCount);

        void ConsumeCancel() => CancelConsumedFrame = Time.frameCount;

        /// <summary>아무것도 잡거나 조준하지 않은 상태.</summary>
        public bool IsIdle => _mode == Mode.Idle;

        /// <summary>가능 여부 표시. 대상 표시를 켜기 전에 끈다.</summary>
        public AvailabilityGlow Availability { get; set; }

        public void Init(BattleSession session, BoardView board)
        {
            _session = session;
            _board = board;
            _arrow = TargetArrow.Create(board.EffectRoot);
            _attackPreview = AttackPreview.Create(board.EffectRoot);
        }

        /// <summary>쓸 보석 미리보기: 끌고 있는 카드, 없으면 마우스를 올린 손패 카드의 코스트.</summary>
        void LateUpdate()
        {
            if (_session == null || _board == null)
                return;
            var card = _held != null && _board.IsInHand(_held) ? _held : _board.HoveredHandCard;
            _board.Hud.Gems.SetPreview(card != null && _session.InProgress ? card.Cost : -1);
        }

        void Update()
        {
            if (_session == null || _board == null)
                return;
            var pointer = Pointer.current;
            if (pointer == null)
                return;

            Vector2 screen = pointer.position.ReadValue();
            bool pressed = pointer.press.wasPressedThisFrame;
            bool released = pointer.press.wasReleasedThisFrame;
            bool cancel = (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)
                          || (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame);

            // 카드 확인 창이 열려 있거나 이번 프레임에 닫혔으면 보드 입력을 받지 않는다.
            var inspect = _board.Hud.Inspect;
            if (_mode == Mode.Idle && (inspect.IsOpen || inspect.ClosedFrame == Time.frameCount))
            {
                ClearHover();
                return;
            }

            // 잡고 있던 카드가 효과로 사라졌으면 손을 놓는다.
            if (_mode == Mode.Pressing ? _pressed == null : _mode != Mode.Idle && _held == null)
            {
                ResetState();
                return;
            }

            switch (_mode)
            {
                case Mode.Idle:
                    UpdateHover(screen);
                    if (pressed && !PointerOverUi())
                        BeginPress(screen);
                    break;
                case Mode.Pressing:
                    // 보드 카드: 끌지 않고 떼면 (누른 시간과 상관없이) 카드 확인. 내 기물을 끌면 공격 화살표.
                    if (cancel)
                    {
                        ConsumeCancel();
                        ResetState();
                    }
                    else if (released)
                    {
                        var card = _pressed;
                        ResetState();
                        Inspect(card);
                    }
                    else if ((screen - _pressPos).magnitude > DragPixels && CanAimFrom(_pressed))
                    {
                        var card = _pressed;
                        ResetState();
                        BeginAim(card);
                    }
                    break;
                case Mode.Dragging:
                    // 끌던 카드는 Esc·오른쪽 클릭으로 손패에 돌려놓는다.
                    if (cancel)
                    {
                        ConsumeCancel();
                        CancelHeld();
                        break;
                    }
                    if (released && !_dragMoved)
                    {
                        var card = _held;
                        CancelHeld();
                        Inspect(card);
                        break;
                    }
                    DragHeld(screen);
                    if (released)
                        DropHeld(screen);
                    break;
                case Mode.Targeting:
                    UpdateArrow(screen);
                    if (cancel)
                    {
                        ConsumeCancel();
                        CancelHeld();
                    }
                    else if (pressed && !PointerOverUi())
                        ChooseTarget(screen);
                    break;
                case Mode.Aiming:
                    UpdateArrow(screen);
                    if (cancel)
                    {
                        ConsumeCancel();
                        ResetState();
                    }
                    else if (released)
                        FinishAttack(screen);
                    break;
            }
        }

        // ------------------------------------------------------------------ 시작

        void BeginPress(Vector2 screen)
        {
            var card = RaycastCard(screen);
            if (card == null)
                return;
            _pressPos = screen;
            _pressTime = Time.unscaledTime;

            // 호버를 끝낸다 (집은 카드는 Held라 손패 따라가기에서 빠지고, 올라온 자리에서 그대로 집힌다).
            ClearHover();

            if (card.OwnerIndex == _session.Human && _board.IsInHand(card))
            {
                _held = card;
                _held.Held = true;
                _dragMoved = false;
                _mode = Mode.Dragging;
                return;
            }

            // 보드 카드·상대 카드: 짧게 떼면 카드 확인, 끌면 (내 기물이면) 공격.
            _pressed = card;
            _mode = Mode.Pressing;
        }

        bool CanAimFrom(CardView card) =>
            card != null && card.OwnerIndex == _session.Human && card.Type == CardType.Minion;

        void Inspect(CardView card)
        {
            if (card == null || !card.FaceUp)
                return;
            var entity = _session.State.FindEntity(card.EntityId);
            if (entity != null)
                _board.Hud.Inspect.Show(entity);
        }

        void BeginAim(CardView card)
        {
            if (card == null || card.OwnerIndex != _session.Human)
                return;
            if (card.Type == CardType.Minion && _session.State.GetPlayer(_session.Human).Minions.FindById(card.EntityId) != null)
            {
                if (!_session.HumanCanAct)
                {
                    Hint(_session.Player.IsPlaying ? "연출이 끝난 뒤에 공격할 수 있습니다." : "내 차례가 아닙니다.");
                    return;
                }
                CollectAttackTargets(card.EntityId);
                if (_validTargets.Count == 0)
                {
                    Hint(FirstAttackError(card.EntityId));
                    return;
                }
                _held = card;
                _mode = Mode.Aiming;
                GlowTargets();
            }
        }

        // ------------------------------------------------------------------ 손패 호버

        /// <summary>내 손패 카드 위에 마우스가 있으면 그 카드를 손패 위로 올린다. 움직임은 BoardView가 매 프레임 따라간다.</summary>
        void UpdateHover(Vector2 screen)
        {
            // 연출 중에는 카드가 연출로 움직이므로 호버하지 않는다.
            var card = PointerOverUi() || _session.Player.IsPlaying ? null : RaycastCard(screen);
            if (card != null && (card.OwnerIndex != _session.Human || !_board.IsInHand(card)))
                card = null;
            // 올라간 카드의 원래 자리에 커서가 남아 있으면 그 카드를 계속 호버한다 (내려왔다 올라가는 떨림 방지).
            if (card == null && _hovered != null && card != _hovered && !PointerOverUi() && !_session.Player.IsPlaying
                && _board.IsInHand(_hovered) && _board.RestContains(_hovered, screen))
                card = _hovered;

            if (card != _hovered)
            {
                _hovered = card;
                _board.HoveredHandCard = card;
                _board.HandFollowUntil = Time.unscaledTime + HoverSettleSeconds;
            }
            if (_hovered != null)
                _board.HandFollowUntil = Time.unscaledTime + HoverSettleSeconds;
        }

        /// <summary>호버를 끝내고 손패가 제자리로 부드럽게 돌아가게 한다.</summary>
        void ClearHover()
        {
            if (_board.HoveredHandCard == null && (object)_hovered == null)
                return;
            _hovered = null;
            _board.HoveredHandCard = null;
            _board.HandFollowUntil = Time.unscaledTime + HoverSettleSeconds;
        }

        /// <summary>호버가 바뀐 뒤 손패가 따라가는 시간.</summary>
        const float HoverSettleSeconds = 0.5f;

        // ------------------------------------------------------------------ 손패 카드 끌기

        void DragHeld(Vector2 screen)
        {
            // 짧은 클릭과 구분: 조금 움직이거나 오래 누르기 전에는 카드를 들어 올리지 않는다.
            if (!_dragMoved && (screen - _pressPos).magnitude <= DragPixels && Time.unscaledTime - _pressTime <= HoldSeconds)
                return;
            _dragMoved = true;
            if (!PointOnPlane(screen, HoldHeight, out var point))
                return;
            var t = _held.transform;
            t.localPosition = Vector3.Lerp(t.localPosition, point, 0.5f);
            t.localRotation = Quaternion.Slerp(t.localRotation, Quaternion.Euler(-30f, 0f, 0f), 0.4f);
            t.localScale = Vector3.Lerp(t.localScale, Vector3.one * DragScale, 0.4f);
        }

        void DropHeld(Vector2 screen)
        {
            if (!PointOnPlane(screen, 0f, out var point) || point.z < BoardView.PlayLineZ)
            {
                CancelHeld();
                return;
            }

            if (!_session.HumanCanAct)
            {
                Hint(_session.Player.IsPlaying ? "연출 중에는 카드를 낼 수 없습니다." : "내 차례가 아닙니다.");
                CancelHeld();
                return;
            }

            var entity = _session.State.GetPlayer(_session.Human).Hand.FindById(_held.EntityId);
            if (entity == null)
            {
                CancelHeld();
                return;
            }
            // 코스트가 모자라면 보석 줄을 흔든다 (이유 안내는 아래 검증 실패 문구로).
            if (entity.Cost > _session.State.GetPlayer(_session.Human).AvailableCost)
                _board.Hud.Gems.Shake(0);

            if (_session.RequiresTarget(entity))
            {
                // 대상이 필요한 카드: 코스트 등 다른 조건을 먼저 확인한 뒤 대상 고르기로.
                CollectPlayTargets(entity.Id);
                if (_validTargets.Count == 0)
                {
                    Hint(FirstPlayError(entity.Id));
                    CancelHeld();
                    return;
                }
                _mode = Mode.Targeting;
                // 사용한 카드는 왼쪽 아래로 치워 대상을 가리지 않게 한다.
                _board.TargetingPose(out var pos, out var rot);
                _held.transform.position = pos;
                _held.transform.rotation = rot;
                _held.transform.localScale = Vector3.one * BoardView.TargetingScale;
                GlowTargets();
                Hint("대상을 고르세요 (오른쪽 클릭 / Esc: 취소)", 3f);
                return;
            }

            // 기물: 놓은 위치에서 가장 가까운 빈 칸에 소환한다 (유희왕처럼 칸을 고름).
            int? position = entity.Type == CardType.Minion
                ? _board.MinionSlotAt(_session.State.GetPlayer(_session.Human).Minions, _session.Human, point.x)
                : null;
            Submit(new PlayCardCommand(_session.Human, entity.Id, null, position));
        }

        void ChooseTarget(Vector2 screen)
        {
            int? target = RaycastTarget(screen);
            if (target == null || !_validTargets.Contains(target.Value))
            {
                Hint("그 대상은 고를 수 없습니다.");
                return;
            }
            Submit(new PlayCardCommand(_session.Human, _held.EntityId, target.Value));
        }

        void CancelHeld()
        {
            var card = _held;
            ResetState();
            if (card != null)
                StartCoroutine(_board.Layout(_session.Human, 0.2f));
        }

        // ------------------------------------------------------------------ 공격

        void FinishAttack(Vector2 screen)
        {
            int? target = RaycastTarget(screen);
            if (target != null && _validTargets.Contains(target.Value))
                Submit(new AttackCommand(_session.Human, _held.EntityId, target.Value));
            else
            {
                if (target != null)
                    Hint(_session.Validate(new AttackCommand(_session.Human, _held.EntityId, target.Value)).Reason);
                ResetState();
            }
        }

        // ------------------------------------------------------------------ 대상

        void CollectAttackTargets(int attackerId)
        {
            _validTargets.Clear();
            var opponent = _session.State.GetOpponent(_session.Human);
            var candidates = new List<int> { opponent.Hero.Id };
            foreach (var m in opponent.Minions.Entities)
                candidates.Add(m.Id);
            foreach (int id in candidates)
            {
                if (_session.Validate(new AttackCommand(_session.Human, attackerId, id)).IsValid)
                    _validTargets.Add(id);
            }
        }

        string FirstAttackError(int attackerId)
        {
            var opponent = _session.State.GetOpponent(_session.Human);
            return _session.Validate(new AttackCommand(_session.Human, attackerId, opponent.Hero.Id)).Reason ?? "공격할 수 없습니다.";
        }

        void CollectPlayTargets(int cardId)
        {
            _validTargets.Clear();
            foreach (int id in Core.AI.LegalCommands.EffectTargets(_session.State))
            {
                if (_session.Validate(new PlayCardCommand(_session.Human, cardId, id)).IsValid)
                    _validTargets.Add(id);
            }
        }

        string FirstPlayError(int cardId)
        {
            var any = Core.AI.LegalCommands.EffectTargets(_session.State);
            return any.Count == 0 ? "고를 대상이 없습니다."
                : _session.Validate(new PlayCardCommand(_session.Human, cardId, any[0])).Reason ?? "낼 수 없습니다.";
        }

        void GlowTargets()
        {
            Availability?.Clear();
            ClearGlow();
            foreach (int id in _validTargets)
            {
                var card = _board.Card(id);
                if (card == null)
                    continue;
                card.SetGlow(ValidGlow);
                _glowing.Add(card);
            }
        }

        void ClearGlow()
        {
            foreach (var card in _glowing)
            {
                if (card != null)
                    card.SetGlow(null);
            }
            _glowing.Clear();
        }

        void UpdateArrow(Vector2 screen)
        {
            if (!PointOnPlane(screen, 0.3f, out var point))
                return;
            int? target = RaycastTarget(screen);
            bool valid = target != null && _validTargets.Contains(target.Value);
            _arrow.Show(_held.transform.position, point, valid);

            // 공격 조준 중 고를 수 있는 대상 위: 예상 피해·처치 미리보기.
            var targetPos = valid && _mode == Mode.Aiming ? _board.PositionOf(_session.State, target.Value) : null;
            if (targetPos.HasValue)
                _attackPreview.Show(_session.State, _held.EntityId, target.Value, _held.transform.position, targetPos.Value);
            else
                _attackPreview.Hide();
        }

        // ------------------------------------------------------------------ 공통

        void Submit(Command command)
        {
            var card = _held;
            ResetState();
            if (!_session.TrySubmit(command, out var reason))
            {
                Hint(reason);
                if (card != null)
                    StartCoroutine(_board.Layout(_session.Human, 0.2f));
            }
        }

        void ResetState()
        {
            if (_held != null)
                _held.Held = false;
            _held = null;
            _pressed = null;
            _mode = Mode.Idle;
            _arrow.Hide();
            _attackPreview.Hide();
            ClearGlow();
            _validTargets.Clear();
        }

        void Hint(string text, float seconds = 1.8f)
        {
            if (!string.IsNullOrEmpty(text))
                _board.Hud.ShowHint(text, seconds);
        }

        static bool PointerOverUi() => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        CardView RaycastCard(Vector2 screen)
        {
            var ray = _board.Camera.ScreenPointToRay(screen);
            var hits = Physics.RaycastAll(ray, 100f);
            CardView best = null;
            float bestDistance = float.MaxValue;
            foreach (var hit in hits)
            {
                var card = hit.collider.GetComponentInParent<CardView>();
                if (card != null && card != _held && hit.distance < bestDistance)
                {
                    best = card;
                    bestDistance = hit.distance;
                }
            }
            return best;
        }

        /// <summary>포인터 아래의 대상 ID (카드 또는 영웅).</summary>
        int? RaycastTarget(Vector2 screen)
        {
            var card = RaycastCard(screen);
            if (card != null)
                return card.EntityId;
            var ray = _board.Camera.ScreenPointToRay(screen);
            foreach (var hit in Physics.RaycastAll(ray, 100f))
            {
                var hero = hit.collider.GetComponentInParent<HeroView>();
                if (hero != null)
                    return _session.State.GetPlayer(hero.OwnerIndex).Hero.Id;
            }
            return null;
        }

        bool PointOnPlane(Vector2 screen, float height, out Vector3 point)
        {
            var ray = _board.Camera.ScreenPointToRay(screen);
            var plane = new Plane(Vector3.up, new Vector3(0f, height, 0f));
            if (plane.Raycast(ray, out float distance))
            {
                point = ray.GetPoint(distance);
                return true;
            }
            point = default;
            return false;
        }
    }
}
