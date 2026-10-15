using System.Collections;
using System.Collections.Generic;
using TCG.Client.Cards;
using TCG.Client.Common;
using TCG.Client.Presentation;
using TCG.Core.State;
using UnityEngine;

namespace TCG.Client.Board
{
    /// <summary>
    /// 2.5D 보드: 테이블, 영역 칸, 카드·영웅·덱 표시.
    /// 화면 쪽 카드 목록(손패·영역별)을 따로 들고 있어서, 연출 중에는 이벤트대로 목록을 바꾸며 카드를 옮기고,
    /// 연출이 끝나면 <see cref="SyncTo"/>로 실제 상태와 맞춘다 (어긋남 = <see cref="LastDriftCount"/>).
    /// </summary>
    public sealed class BoardView : MonoBehaviour
    {
        static readonly Color Table = new Color(0.13f, 0.2f, 0.17f);
        static readonly Color Slot = new Color(0.19f, 0.28f, 0.24f);
        static readonly Color Divider = new Color(0.32f, 0.27f, 0.18f);
        static readonly Color ZoneLabel = new Color(1f, 1f, 1f, 0.35f);

        static readonly ZoneType[] BoardZones = { ZoneType.Minion, ZoneType.Field, ZoneType.Companion, ZoneType.Summon };

        readonly Dictionary<int, CardView> _cards = new Dictionary<int, CardView>();
        readonly List<CardView>[] _hands = { new List<CardView>(), new List<CardView>() };
        readonly Dictionary<ZoneType, List<CardView>>[] _zones =
        {
            NewZoneLists(), NewZoneLists(),
        };
        readonly HeroView[] _heroes = new HeroView[2];
        readonly PileView[] _piles = new PileView[2];
        Transform _cardRoot;

        public Camera Camera { get; private set; }
        public HudView Hud { get; private set; }
        /// <summary>카드 등장·퇴장 사운드 (없으면 소리 없음).</summary>
        public TCG.Client.UserAssets.CardSoundPlayer Sounds { get; set; }
        public Transform EffectRoot { get; private set; }
        public int Viewer { get; private set; }
        /// <summary>마지막 <see cref="SyncTo"/>에서 화면 표시와 실제 상태가 달랐던 수 (연출 검증용).</summary>
        public int LastDriftCount { get; private set; }

        static Dictionary<ZoneType, List<CardView>> NewZoneLists()
        {
            var d = new Dictionary<ZoneType, List<CardView>>();
            foreach (var z in BoardZones)
                d[z] = new List<CardView>();
            return d;
        }

        public static BoardView Create(Transform parent, Camera camera, int viewer)
        {
            var go = new GameObject("Board");
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<BoardView>();
            view.Viewer = viewer;
            view.Build(camera);
            return view;
        }

        void Build(Camera camera)
        {
            Camera = camera;
            Shapes.Box(transform, "Table", new Vector3(0, -0.06f, 0), new Vector3(16.5f, 0.1f, 12.5f), Table);
            Shapes.Box(transform, "Divider", new Vector3(0, -0.005f, 0), new Vector3(15.5f, 0.01f, 0.04f), Divider);

            for (int side = 0; side < 2; side++)
            {
                bool bottom = side == 0;
                var slots = new GameObject(bottom ? "Slots (나)" : "Slots (상대)").transform;
                slots.SetParent(transform, false);

                for (int i = 0; i < BoardLayout.MinionSlots; i++)
                    SlotPlate(slots, BoardLayout.Minion(i, bottom));
                SlotPlate(slots, BoardLayout.Field(bottom));
                for (int i = 0; i < 4; i++)
                {
                    SlotPlate(slots, BoardLayout.Companion(i, bottom));
                    SlotPlate(slots, BoardLayout.Summon(i, bottom));
                }

                ZoneName(slots, "기물", BoardLayout.Minion(BoardLayout.MinionSlots / 2, bottom), bottom);
                ZoneName(slots, "필드", BoardLayout.Field(bottom), bottom, outward: true);
                ZoneName(slots, "동료", Vector3.Lerp(BoardLayout.Companion(0, bottom), BoardLayout.Companion(1, bottom), 0.5f), bottom);
                ZoneName(slots, "소환물", Vector3.Lerp(BoardLayout.Summon(0, bottom), BoardLayout.Summon(1, bottom), 0.5f), bottom);

                _heroes[side] = HeroView.Create(transform, bottom ? Viewer : 1 - Viewer, bottom ? "나" : "상대", BoardLayout.Hero(bottom),
                    bottom ? new Color(0.25f, 0.42f, 0.62f) : new Color(0.62f, 0.27f, 0.25f));
                _piles[side] = PileView.Create(transform, BoardLayout.Deck(bottom));
            }

            _cardRoot = new GameObject("Cards").transform;
            _cardRoot.SetParent(transform, false);
            EffectRoot = new GameObject("Effects").transform;
            EffectRoot.SetParent(transform, false);
            Hud = HudView.Create(transform, camera);
        }

        static void SlotPlate(Transform parent, Vector3 position)
        {
            Shapes.Box(parent, "Slot", position + new Vector3(0, -0.005f, 0),
                new Vector3(CardView.Width + 0.1f, 0.01f, CardView.Height + 0.1f), Slot);
        }

        /// <summary>칸 이름. 기본은 칸의 가운데 쪽(구분선 쪽), outward면 바깥쪽(뒷줄 칸: 앞줄과 겹치지 않게).</summary>
        static void ZoneName(Transform parent, string text, Vector3 slotCenter, bool bottom, bool outward = false)
        {
            float toward = outward ? -0.9f : 0.9f;
            var offset = new Vector3(0, 0.002f, bottom ? toward : -toward);
            Shapes.Label(parent, $"Label {text}", text, slotCenter + offset, 2.2f, ZoneLabel, 2f, 0.25f);
        }

        // ------------------------------------------------------------------ 조회

        /// <summary>0 = 화면 아래(나), 1 = 위(상대).</summary>
        public int SideOf(int ownerIndex) => ownerIndex == Viewer ? 0 : 1;
        public HeroView HeroOf(int ownerIndex) => _heroes[SideOf(ownerIndex)];
        public PileView PileOf(int ownerIndex) => _piles[SideOf(ownerIndex)];
        public Vector3 DeckPosition(int ownerIndex) => BoardLayout.Deck(SideOf(ownerIndex) == 0) + Vector3.up * 0.4f;
        public Vector3 HeroPosition(int ownerIndex) => BoardLayout.Hero(SideOf(ownerIndex) == 0);
        public IReadOnlyCollection<CardView> AllCards => _cards.Values;

        public CardView Card(int entityId) => _cards.TryGetValue(entityId, out var v) ? v : null;

        /// <summary>대상 ID의 위치 (영웅 또는 카드). 없으면 null.</summary>
        public Vector3? PositionOf(GameState anyState, int id)
        {
            var card = Card(id);
            if (card != null)
                return card.transform.position;
            var hero = anyState.FindHero(id);
            return hero != null ? HeroOf(hero.OwnerIndex).transform.position : (Vector3?)null;
        }

        /// <summary>화면 가운데 위, 카메라를 향한 "크게 보여 주기" 자세.</summary>
        public void ShowcasePose(out Vector3 position, out Quaternion rotation, float offsetX = 0f)
        {
            var target = new Vector3(0, 0, -0.9f);
            position = Vector3.Lerp(target, Camera.transform.position, 0.42f) + Camera.transform.right * offsetX;
            rotation = Quaternion.LookRotation(Camera.transform.up, -Camera.transform.forward);
        }

        /// <summary>
        /// 대상 지정 중 사용한 카드를 둘 자리: 화면 왼쪽 아래 빈 곳, 카메라를 향해 작게 (보드·대상을 가리지 않게, GameDesign §11.2).
        /// </summary>
        public void TargetingPose(out Vector3 position, out Quaternion rotation)
        {
            position = Camera.ViewportToWorldPoint(new Vector3(TargetingViewport.x, TargetingViewport.y, TargetingDistance));
            rotation = Quaternion.LookRotation(Camera.transform.up, -Camera.transform.forward);
        }

        // ------------------------------------------------------------------ 손패 호버 (GameDesign §11.2)
        // 슬레이 더 스파이어·하스스톤처럼: 올린 카드는 제자리에서 부채꼴 기울기를 펴고 위로 올라오며 조금 커진다.
        // 양옆 카드는 살짝 비켜난다. 호버 중과 끝난 직후에는 내 손패 전체가 매 프레임 목표 자세로 부드럽게 따라간다.

        /// <summary>마우스를 올린 내 손패 카드. null이면 없음.</summary>
        public CardView HoveredHandCard { get; set; }
        /// <summary>이 시각(unscaled)까지 내 손패를 매 프레임 따라가게 한다. 입력이 호버 중 계속 늘린다.</summary>
        public float HandFollowUntil { get; set; }

        public const float HoverScale = 1.45f;
        /// <summary>올라오는 거리 (카드 면 위쪽 방향).</summary>
        const float HoverLift = 0.95f;
        /// <summary>앞으로(위로) 띄워 옆 카드 위에 그려지게.</summary>
        const float HoverRaise = 0.35f;
        /// <summary>양옆 카드가 비켜나는 거리.</summary>
        const float HoverSpread = 0.4f;
        const float HandFollowSpeed = 16f;

        /// <summary>호버한 손패 카드의 자세와 크기.</summary>
        public void HoverPose(CardView view, out Vector3 position, out Quaternion rotation, out float scale)
        {
            int side = SideOf(view.OwnerIndex);
            int index = Mathf.Max(0, _hands[side].IndexOf(view));
            BoardLayout.Hand(index, _hands[side].Count, side == 0, out var rest, out _);
            rotation = Quaternion.Euler(-35f, 0f, 0f); // 부채꼴 기울기 없이 똑바로
            position = new Vector3(rest.x, rest.y + HoverRaise, rest.z) + rotation * Vector3.forward * HoverLift;
            scale = HoverScale;
        }

        /// <summary>
        /// 화면 좌표가 이 손패 카드의 제자리(손패에 놓인 자세) 안인가. 호버로 카드가 올라가도 원래 자리에 커서가 있으면
        /// 호버를 유지해서, 카드가 내려왔다 올라갔다 떨리지 않게 한다.
        /// </summary>
        public bool RestContains(CardView view, Vector2 screen)
        {
            if (!TargetPose(view, out var pos, out var rot))
                return false;
            float w = CardView.Width * 0.5f, h = CardView.Height * 0.5f;
            var a = (Vector2)Camera.WorldToScreenPoint(pos + rot * new Vector3(-w, 0, -h));
            var b = (Vector2)Camera.WorldToScreenPoint(pos + rot * new Vector3(w, 0, -h));
            var c = (Vector2)Camera.WorldToScreenPoint(pos + rot * new Vector3(w, 0, h));
            var d = (Vector2)Camera.WorldToScreenPoint(pos + rot * new Vector3(-w, 0, h));
            return InTriangle(screen, a, b, c) || InTriangle(screen, a, c, d);
        }

        static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Cross(p, a, b), d2 = Cross(p, b, c), d3 = Cross(p, c, a);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }

        static float Cross(Vector2 p, Vector2 a, Vector2 b) => (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);

        /// <summary>호버 중 옆 카드가 비켜나는 X 거리 (호버 카드 왼쪽은 -, 오른쪽은 +).</summary>
        float HandSpread(CardView view, int side)
        {
            var hovered = HoveredHandCard;
            if (hovered == null || hovered == view || side != 0)
                return 0f;
            int h = _hands[0].IndexOf(hovered), i = _hands[0].IndexOf(view);
            if (h < 0 || i < 0)
                return 0f;
            return i < h ? -HoverSpread : HoverSpread;
        }

        void Update()
        {
            if (Time.unscaledTime > HandFollowUntil)
                return;
            float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime * HandFollowSpeed);
            foreach (var view in _hands[0])
            {
                if (view == null || view.Held)
                    continue;
                Vector3 pos;
                Quaternion rot;
                float scale = 1f;
                if (view == HoveredHandCard)
                    HoverPose(view, out pos, out rot, out scale);
                else if (!TargetPose(view, out pos, out rot))
                    continue;
                var t = view.transform;
                t.position = Vector3.Lerp(t.position, pos, k);
                t.rotation = Quaternion.Slerp(t.rotation, rot, k);
                t.localScale = Vector3.Lerp(t.localScale, Vector3.one * scale, k);
            }
        }

        static readonly Vector2 TargetingViewport = new Vector2(0.09f, 0.25f);
        const float TargetingDistance = 8f;
        public const float TargetingScale = 0.85f;

        /// <summary>
        /// 기물을 world X 위치에 놓을 때 소환할 칸 번호: 놓은 위치에서 가장 가까운 빈 칸 (GameDesign §4.1). 빈 칸이 없으면 null.
        /// </summary>
        public int? MinionSlotAt(Zone minions, int ownerIndex, float worldX)
        {
            bool bottom = SideOf(ownerIndex) == 0;
            int? best = null;
            float bestDistance = float.MaxValue;
            for (int slot = 0; slot < BoardLayout.MinionSlots && slot < minions.Capacity; slot++)
            {
                if (!minions.IsSlotFree(slot))
                    continue;
                float distance = Mathf.Abs(BoardLayout.Minion(slot, bottom).x - worldX);
                if (distance < bestDistance)
                {
                    best = slot;
                    bestDistance = distance;
                }
            }
            return best;
        }

        /// <summary>이 Z보다 위(상대 쪽)에 카드를 놓으면 "보드에 냄"으로 본다.</summary>
        public const float PlayLineZ = -4.1f;

        // ------------------------------------------------------------------ 카드 목록 조작 (연출용)

        public CardView EnsureCard(Entity entity, bool faceUp, Vector3 position, Quaternion rotation)
        {
            if (!_cards.TryGetValue(entity.Id, out var view))
            {
                view = CardView.Create(_cardRoot, entity, faceUp);
                view.transform.localPosition = position;
                view.transform.localRotation = rotation;
                _cards[entity.Id] = view;
            }
            else if (view.FaceUp != faceUp)
            {
                view.SetFaceUp(faceUp);
            }
            return view;
        }

        /// <summary>가려진 채 그려진 카드 중 state에서 정체가 보이는 것을 앞면으로 다시 만든다 (온라인 시야).</summary>
        public void RevealFrom(GameState state)
        {
            foreach (var view in _cards.Values)
            {
                var entity = state.GetEntity(view.EntityId);
                if (entity != null)
                    view.Reveal(entity);
            }
        }

        /// <summary>목록에서만 뺀다 (화면에는 남음 — 연출이 끝나면 직접 지운다).</summary>
        public void Detach(CardView view)
        {
            for (int side = 0; side < 2; side++)
            {
                _hands[side].Remove(view);
                foreach (var z in BoardZones)
                    _zones[side][z].Remove(view);
            }
        }

        public void Destroy(CardView view)
        {
            if (view == null)
                return;
            Detach(view);
            _cards.Remove(view.EntityId);
            Shapes.DestroyImmediateOrDeferred(view.gameObject);
        }

        public void AddToHand(CardView view)
        {
            Detach(view);
            _hands[SideOf(view.OwnerIndex)].Add(view);
        }

        public void InsertInZone(CardView view, ZoneType zone, int index)
        {
            Detach(view);
            var list = _zones[SideOf(view.OwnerIndex)][zone];
            list.Insert(Mathf.Clamp(index, 0, list.Count), view);
        }

        /// <summary>화면 쪽(나) 손패에서 index번째 카드. 없으면 null.</summary>
        public CardView BottomHandCard(int index) => index >= 0 && index < _hands[0].Count ? _hands[0][index] : null;

        public bool IsInHand(CardView view) => _hands[SideOf(view.OwnerIndex)].Contains(view);

        // ------------------------------------------------------------------ 배치

        /// <summary>목록상 위치에 해당하는 자세. 목록에 없으면 false.</summary>
        public bool TargetPose(CardView view, out Vector3 position, out Quaternion rotation)
        {
            int side = SideOf(view.OwnerIndex);
            bool bottom = side == 0;
            position = Vector3.zero;
            rotation = Quaternion.identity;

            int handIndex = _hands[side].IndexOf(view);
            if (handIndex >= 0)
            {
                BoardLayout.Hand(handIndex, _hands[side].Count, bottom, out position, out rotation);
                position.x += HandSpread(view, side);
                return true;
            }

            foreach (var zone in BoardZones)
            {
                var list = _zones[side][zone];
                int i = list.IndexOf(view);
                if (i < 0)
                    continue;
                switch (zone)
                {
                    // 기물은 자기 칸 번호의 칸 표시 위에. 칸 번호를 모르면(옛 상태) 가운데 정렬.
                    case ZoneType.Minion:
                        position = view.Slot >= 0 && view.Slot < BoardLayout.MinionSlots
                            ? BoardLayout.Minion(view.Slot, bottom)
                            : BoardLayout.Minion(i, list.Count, bottom);
                        break;
                    case ZoneType.Field: position = BoardLayout.Field(bottom); break;
                    case ZoneType.Companion: position = BoardLayout.Companion(i, bottom); break;
                    case ZoneType.Summon: position = BoardLayout.Summon(i, bottom); break;
                }
                return true;
            }
            return false;
        }

        /// <summary>한쪽 플레이어의 손패·보드 카드를 목록상 자리로 옮긴다. except는 건드리지 않는다.</summary>
        public IEnumerator Layout(int ownerIndex, float duration, CardView except = null)
        {
            int side = SideOf(ownerIndex);
            var moves = new List<IEnumerator>();
            var all = new List<CardView>(_hands[side]);
            foreach (var z in BoardZones)
                all.AddRange(_zones[side][z]);
            foreach (var view in all)
            {
                if (view == except || view.Held || !TargetPose(view, out var pos, out var rot))
                    continue;
                moves.Add(Tween.Move(view.transform, pos, rot, duration, 0f, Vector3.one));
            }
            yield return Tween.All(this, moves);
        }

        // ------------------------------------------------------------------ 실제 상태와 맞추기

        /// <summary>연출 없이 즉시 상태 그대로 배치.</summary>
        public void Render(GameState state)
        {
            var e = SyncTo(state, 0f, initial: true);
            while (e.MoveNext()) { }
        }

        /// <summary>
        /// 실제 상태에 맞게 목록을 다시 짜고 숫자를 맞춘 뒤 자리로 옮긴다. 상대 손패는 뒷면.
        /// 표시가 달랐던 수를 <see cref="LastDriftCount"/>에 남긴다.
        /// </summary>
        public IEnumerator SyncTo(GameState state, float duration, bool initial = false)
        {
            int drift = 0;
            var seen = new HashSet<int>();
            for (int side = 0; side < 2; side++)
            {
                _hands[side].Clear();
                foreach (var z in BoardZones)
                    _zones[side][z].Clear();
            }

            for (int side = 0; side < 2; side++)
            {
                var player = state.GetPlayer(side == 0 ? Viewer : 1 - Viewer);
                foreach (var entity in player.Hand.Entities)
                {
                    var view = Track(entity, side == 0, seen, initial, ref drift);
                    _hands[side].Add(view);
                }
                foreach (var z in BoardZones)
                {
                    foreach (var entity in player.GetZone(z).Entities)
                    {
                        var view = Track(entity, true, seen, initial, ref drift);
                        view.Slot = entity.Slot;
                        _zones[side][z].Add(view);
                    }
                }

                if (_heroes[side].Refresh(player.Hero))
                    drift++;
                _piles[side].Refresh(player.Deck.Count, player.Graveyard.Count, player.Exile.Count);
            }

            var stale = new List<int>();
            foreach (var id in _cards.Keys)
            {
                if (!seen.Contains(id))
                    stale.Add(id);
            }
            stale.Sort();
            foreach (var id in stale)
            {
                // 연출이 치웠어야 할 카드가 남아 있었다.
                if (!initial)
                    drift++;
                Destroy(_cards[id]);
            }

            Hud.Refresh(state, Viewer);
            LastDriftCount = drift;

            if (duration <= 0f)
            {
                foreach (var view in _cards.Values)
                {
                    if (!view.Held && TargetPose(view, out var pos, out var rot))
                    {
                        view.transform.localPosition = pos;
                        view.transform.localRotation = rot;
                        view.transform.localScale = Vector3.one;
                    }
                }
                yield break;
            }

            yield return Tween.All(this, new[] { Layout(Viewer, duration), Layout(1 - Viewer, duration) });
        }

        CardView Track(Entity entity, bool faceUp, HashSet<int> seen, bool initial, ref int drift)
        {
            seen.Add(entity.Id);
            bool existed = _cards.ContainsKey(entity.Id);
            // 연출이 만들었어야 할 카드가 없었거나, 표시 숫자가 실제와 달랐다.
            if (!existed && !initial)
                drift++;
            var view = EnsureCard(entity, faceUp, BoardLayout.Deck(SideOf(entity.OwnerIndex) == 0), Quaternion.identity);
            if (view.SetNumbersFrom(entity) && existed)
                drift++;
            view.SetGlow(null);
            return view;
        }
    }
}
