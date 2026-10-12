namespace TCG.Core.Data
{
    /// <summary>효과 발동 방식 (GameDesign §7.2). 카드를 만들 때 효과마다 고른다.</summary>
    public enum EffectActivation
    {
        /// <summary>강제: 발동 조건이 되면 반드시 체인에 올라간다.</summary>
        Mandatory,
        /// <summary>선택: 발동 조건이 되면 발동할지 플레이어가 고른다.</summary>
        Optional,
    }
}
