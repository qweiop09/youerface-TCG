using UnityEngine;

namespace TCG.Client.Board
{
    /// <summary>
    /// 보드 위 영역 칸의 위치 (월드 좌표, 바닥 = XZ 평면). 아래쪽(-Z)이 화면을 보는 플레이어, 위쪽(+Z)이 상대.
    ///
    ///   앞줄:  [동료][동료]   [기물][기물][기물][기물][기물]   [소환물][소환물]
    ///   뒷줄:  [동료][동료]     [필드]      [영웅]            [소환물][소환물]
    ///                                                                       [덱]   ← 소환물 바깥, 뒷줄보다 한 칸 아래
    /// 앞줄은 싸우는 기물만 두고, 필드는 영웅 옆(뒷줄)에 둔다. 동료·소환물 묶음은 기물 줄과 0.5 띄워 구분한다.
    /// 상대 쪽은 Z를 뒤집은 같은 배치.
    /// </summary>
    public static class BoardLayout
    {
        /// <summary>기물 칸 간격 (카드 폭 1 + 여백 0.4).</summary>
        public const float SlotGap = 1.4f;

        const float FrontRowZ = 1.15f;
        const float BackRowZ = 2.85f;
        const float HeroZ = 3.05f;
        const float HandZ = 5.35f;

        static readonly float[] MinionX = { -2 * SlotGap, -SlotGap, 0f, SlotGap, 2 * SlotGap };
        /// <summary>영웅 왼쪽 뒷줄.</summary>
        const float FieldX = -2.35f;
        /// <summary>안쪽 열, 바깥 열. 안쪽 열 = 기물 끝에서 묶음 간격만큼.</summary>
        static readonly float[] CompanionX = { -4.3f, -5.5f };
        static readonly float[] SummonX = { 4.3f, 5.5f };
        const float DeckX = 6.85f;
        const float DeckZ = 4.25f;

        /// <summary>bottom=true면 화면 쪽(나), false면 상대 쪽. Z 부호를 뒤집는다.</summary>
        static Vector3 P(float x, float z, bool bottom) => new Vector3(x, 0f, bottom ? -z : z);

        /// <summary>빈 칸 표시용 고정 위치 (5칸).</summary>
        public static Vector3 Minion(int index, bool bottom) => P(MinionX[index], FrontRowZ, bottom);

        /// <summary>기물 count개를 가운데 정렬했을 때 index번째 위치.</summary>
        public static Vector3 Minion(int index, int count, bool bottom) =>
            P((index - (count - 1) * 0.5f) * SlotGap, FrontRowZ, bottom);
        public static Vector3 Field(bool bottom) => P(FieldX, BackRowZ, bottom);

        /// <summary>2x2 칸: 0,1 = 앞줄(안쪽→바깥), 2,3 = 뒷줄.</summary>
        public static Vector3 Companion(int index, bool bottom) =>
            P(CompanionX[index % 2], index < 2 ? FrontRowZ : BackRowZ, bottom);

        public static Vector3 Summon(int index, bool bottom) =>
            P(SummonX[index % 2], index < 2 ? FrontRowZ : BackRowZ, bottom);

        public static Vector3 Hero(bool bottom) => P(0f, HeroZ, bottom);
        public static Vector3 Deck(bool bottom) => P(DeckX, DeckZ, bottom);
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
