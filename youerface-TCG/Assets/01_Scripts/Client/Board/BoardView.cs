using System.Collections.Generic;
using TCG.Client.Cards;
using TCG.Client.Common;
using TCG.Core.State;
using UnityEngine;

namespace TCG.Client.Board
{
    /// <summary>
    /// 2.5D 보드: 테이블, 영역 칸, 카드·영웅·덱 표시. <see cref="Render"/>는 현재 상태에 맞게 즉시 배치한다
    /// (연출 없이 "지금 상태 그대로"). 연출 재생은 다음 단계에서 이 위에 얹는다.
    /// </summary>
    public sealed class BoardView : MonoBehaviour
    {
        static readonly Color Table = new Color(0.13f, 0.2f, 0.17f);
        static readonly Color Slot = new Color(0.19f, 0.28f, 0.24f);
        static readonly Color Divider = new Color(0.32f, 0.27f, 0.18f);
        static readonly Color ZoneLabel = new Color(1f, 1f, 1f, 0.35f);

        readonly Dictionary<int, CardView> _cards = new Dictionary<int, CardView>();
        readonly HeroView[] _heroes = new HeroView[2];
        readonly PileView[] _piles = new PileView[2];
        Transform _cardRoot;
        HudView _hud;

        public static BoardView Create(Transform parent, Camera camera)
        {
            var go = new GameObject("Board");
            go.transform.SetParent(parent, false);
            var view = go.AddComponent<BoardView>();
            view.Build(camera);
            return view;
        }

        void Build(Camera camera)
        {
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

                ZoneName(slots, "기물", BoardLayout.Minion(1, bottom), bottom);
                ZoneName(slots, "필드", BoardLayout.Field(bottom), bottom);
                ZoneName(slots, "동료", Vector3.Lerp(BoardLayout.Companion(0, bottom), BoardLayout.Companion(1, bottom), 0.5f), bottom);
                ZoneName(slots, "소환물", Vector3.Lerp(BoardLayout.Summon(0, bottom), BoardLayout.Summon(1, bottom), 0.5f), bottom);

                _heroes[side] = HeroView.Create(transform, bottom ? "나" : "상대", BoardLayout.Hero(bottom),
                    bottom ? new Color(0.25f, 0.42f, 0.62f) : new Color(0.62f, 0.27f, 0.25f));
                _piles[side] = PileView.Create(transform, BoardLayout.Deck(bottom));
            }

            _cardRoot = new GameObject("Cards").transform;
            _cardRoot.SetParent(transform, false);
            _hud = HudView.Create(transform, camera);
        }

        static void SlotPlate(Transform parent, Vector3 position)
        {
            Shapes.Box(parent, "Slot", position + new Vector3(0, -0.005f, 0),
                new Vector3(CardView.Width + 0.1f, 0.01f, CardView.Height + 0.1f), Slot);
        }

        static void ZoneName(Transform parent, string text, Vector3 slotCenter, bool bottom)
        {
            // 영역 이름은 칸 바깥쪽(가운데 선에서 먼 쪽이 아니라 가까운 쪽)에 작게 적는다.
            var offset = new Vector3(0, 0.002f, bottom ? 0.9f : -0.9f);
            Shapes.Label(parent, $"Label {text}", text, slotCenter + offset, 2.2f, ZoneLabel, 2f, 0.25f);
        }

        /// <summary>현재 상태를 그대로 배치한다. viewer = 화면 아래쪽 플레이어. 상대 손패는 뒷면.</summary>
        public void Render(GameState state, int viewer)
        {
            var seen = new HashSet<int>();
            for (int side = 0; side < 2; side++)
            {
                bool bottom = side == 0;
                var player = state.GetPlayer(bottom ? viewer : 1 - viewer);

                for (int i = 0; i < player.Minions.Count; i++)
                    Place(player.Minions.Entities[i], true, BoardLayout.Minion(MinionSlot(i, player.Minions.Count), bottom), Quaternion.identity, seen);
                for (int i = 0; i < player.Field.Count; i++)
                    Place(player.Field.Entities[i], true, BoardLayout.Field(bottom), Quaternion.identity, seen);
                for (int i = 0; i < player.Companions.Count; i++)
                    Place(player.Companions.Entities[i], true, BoardLayout.Companion(i, bottom), Quaternion.identity, seen);
                for (int i = 0; i < player.Summons.Count; i++)
                    Place(player.Summons.Entities[i], true, BoardLayout.Summon(i, bottom), Quaternion.identity, seen);

                int handCount = player.Hand.Count;
                for (int i = 0; i < handCount; i++)
                {
                    BoardLayout.Hand(i, handCount, bottom, out var pos, out var rot);
                    Place(player.Hand.Entities[i], bottom, pos, rot, seen);
                }

                _heroes[side].Refresh(player.Hero);
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
                Shapes.DestroyImmediateOrDeferred(_cards[id].gameObject);
                _cards.Remove(id);
            }

            _hud.Refresh(state, viewer);
        }

        /// <summary>기물이 3칸보다 적으면 가운데로 모은다 (1개 = 가운데 칸, 2개 = 가운데 양옆이 아니라 왼쪽 두 칸).</summary>
        static int MinionSlot(int index, int count) => count == 1 ? 1 : index;

        void Place(Entity entity, bool faceUp, Vector3 position, Quaternion rotation, HashSet<int> seen)
        {
            seen.Add(entity.Id);
            if (!_cards.TryGetValue(entity.Id, out var view) || view.FaceUp != faceUp)
            {
                if (view != null)
                    Shapes.DestroyImmediateOrDeferred(view.gameObject);
                view = CardView.Create(_cardRoot, entity, faceUp);
                _cards[entity.Id] = view;
            }

            view.transform.localPosition = position;
            view.transform.localRotation = rotation;
            view.Refresh(entity);
        }
    }
}
