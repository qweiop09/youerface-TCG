namespace TCG.Core.Commands
{
    /// <summary>플레이어 입력 1건. 검증·적용은 <see cref="Rules.GameEngine"/>이 한다.</summary>
    public abstract class Command
    {
        public int PlayerIndex { get; }

        protected Command(int playerIndex)
        {
            PlayerIndex = playerIndex;
        }
    }

    /// <summary>손패의 카드를 낸다.</summary>
    public sealed class PlayCardCommand : Command
    {
        public int CardId { get; }
        /// <summary>"선택 대상" 효과의 대상 (영웅 또는 보드 위 카드 ID).</summary>
        public int? TargetId { get; }
        /// <summary>기물을 놓을 칸 위치. null이면 맨 끝.</summary>
        public int? Position { get; }

        public PlayCardCommand(int playerIndex, int cardId, int? targetId = null, int? position = null)
            : base(playerIndex)
        {
            CardId = cardId;
            TargetId = targetId;
            Position = position;
        }
    }

    /// <summary>내 기물로 상대 기물 또는 영웅을 공격한다.</summary>
    public sealed class AttackCommand : Command
    {
        public int AttackerId { get; }
        public int TargetId { get; }

        public AttackCommand(int playerIndex, int attackerId, int targetId) : base(playerIndex)
        {
            AttackerId = attackerId;
            TargetId = targetId;
        }
    }

    public sealed class EndTurnCommand : Command
    {
        public EndTurnCommand(int playerIndex) : base(playerIndex) { }
    }
}
