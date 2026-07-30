namespace AlgoForge.ViewModels.Claim
{
    // The one-field "confirm your email" page an unauthenticated invite link lands on --
    // see ClaimController for why this exists instead of a login wall.
    public class ClaimVerifyViewModel
    {
        public string Token { get; set; } = string.Empty;
        public string EventName { get; set; } = string.Empty;
        public string InviteUrl { get; set; } = string.Empty;
        public string? Error { get; set; }
    }
}
