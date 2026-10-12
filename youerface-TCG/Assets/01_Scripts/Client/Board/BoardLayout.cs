using UnityEngine;

namespace TCG.Client.Board
{
    /// <summary>
    /// 보드 위 영역 칸의 위치 (월드 좌표, 바닥 = XZ 평면). 아래쪽(-Z)이 화면을 보는 플레이어, 위쪽(+Z)이 상대.
    ///
    ///   [동료 2x2] [필드] [기물][기물][기물] [소환물 2x2] [덱]
    ///                        [영웅]
    /// 상대 쪽은 Z를 뒤집은 같은 배치.
    /// </summary>
    public static class BoardLayout
    {
        public const float SlotGap = 1.25f;

        const float FrontRowZ = 1.15f;
        const float BackRowZ = 2.85f;
        const float HeroZ = 3.05f;
        const float HandZ = 5.35f;

        static readonly float[] MinionX = { -SlotGap, 0f, SlotGap };
        const float FieldX = -3.25f;
        static readonly float[] CompanionX = { -6.1f, -4.85f };
        static readonly float[] SummonX = { 3.25f, 4.5f };
        const float DeckX = 6.3f;

        /// <summary>bottom=true면 화면 쪽(나), false면 상대 쪽. Z 부호를 뒤집는다.</summary>
        static Vector3 P(float x, float z, bool bottom) => new Vector3(x, 0f, bottom ? -z : z);

        public static Vector3 Minion(int index, bool bottom) => P(MinionX[index], FrontRowZ, bottom);
        public static Vector3 Field(bool bottom) => P(FieldX, FrontRowZ, bottom);

        /// <summary>2x2 칸: 0,1 = 앞줄, 2,3 = 뒷줄.</summary>
        public static Vector3 Companion(int index, bool bottom) =>
            P(CompanionX[index % 2], index < 2 ? FrontRowZ : BackRowZ, bottom);

        public static Vector3 Summon(int index, bool bottom) =>
            P(SummonX[index % 2], index < 2 ? FrontRowZ : BackRowZ, bottom);

        public static Vector3 Hero(bool bottom) => P(0f, HeroZ, bottom);
        public static Vector3 Deck(bool bottom) => P(DeckX, BackRowZ, bottom);
        public static Vector3 HandCenter(bool bottom) => P(0f, HandZ, bottom);

        public static int MinionSlots => MinionX.Length;

        /// <summary>손패 n장 중 i번째 카드의 위치·회전 (부채꼴).</summary>
        public static void Hand(int index, int count, bool bottom, out Vector3 position, out Quaternion rotation)
        {
            float spacing = count <= 6 ? 1.05f : 6.3f / (count - 1);
            float offset = index - (count - 1) * 0.5f;
            var center = HandCenter(bottom);
            float curve = Mathf.Abs(offset) * Mathf.Abs(offset) * 0.03f;
            // 내 손패는 화면 쪽으로 세우므로 아래쪽 끝이 테이블에 묻히지 않게 띄운다.
            float lift = bottom ? 0.55f : 0.05f;
            position = center + new Vector3(offset * spacing, lift + index * 0.004f, bottom ? curve : -curve);

            float fan = -offset * 3f;
            // 내 손패는 화면 쪽으로 세워서 읽기 쉽게 한다.
            rotation = bottom ? Quaternion.Euler(-35f, 0f, 0f) * Quaternion.Euler(0f, fan, 0f)
                              : Quaternion.Euler(0f, 180f - fan, 0f);
        }
    }
}
