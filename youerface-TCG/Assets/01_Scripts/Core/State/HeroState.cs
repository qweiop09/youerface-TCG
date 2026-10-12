namespace TCG.Core.State
{
    /// <summary>
    /// 영웅. 현재는 하스스톤식 체력만 가진다. 영웅 능력·직업 등은 미정(GameDesign Q11).
    /// ID는 카드 Entity와 같은 번호 체계를 써서 공격 대상 지정 시 함께 다룰 수 있다.
    /// </summary>
    public sealed class HeroState
    {
        public int Id { get; }
        public int OwnerIndex { get; }
        public int Health { get; set; }
        public int MaxHealth { get; set; }

        public HeroState(int id, int ownerIndex, int maxHealth)
        {
            Id = id;
            OwnerIndex = ownerIndex;
            MaxHealth = maxHealth;
            Health = maxHealth;
        }
    }
}
