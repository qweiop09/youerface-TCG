namespace TCG.Core.Data
{
    /// <summary>같은 소환물이 또 생성될 때의 처리. 소환물마다 카드 옵션으로 고른다 (GameDesign §4.3).</summary>
    public enum SummonDuplicateMode
    {
        /// <summary>하나 더 놓는다. 칸이 가득 차면 무시.</summary>
        AddNew,
        /// <summary>새로 놓지 않고 기존 소환물의 사용 횟수를 처음 값으로 다시 채운다.</summary>
        RefreshUsage,
    }
}
