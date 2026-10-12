namespace TCG.Core.Rules
{
    public readonly struct ValidationResult
    {
        public bool IsValid { get; }
        /// <summary>실패 사유 (UI 표시용). 성공이면 null.</summary>
        public string Reason { get; }

        ValidationResult(bool isValid, string reason)
        {
            IsValid = isValid;
            Reason = reason;
        }

        public static ValidationResult Ok => new ValidationResult(true, null);
        public static ValidationResult Fail(string reason) => new ValidationResult(false, reason);

        public override string ToString() => IsValid ? "Ok" : "Fail: " + Reason;
    }
}
