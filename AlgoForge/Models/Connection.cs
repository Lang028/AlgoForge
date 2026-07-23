namespace AlgoForge.Models
{
    public class Connection
    {
        public int Id { get; set; }

        public int EventId { get; set; }
        public Event Event { get; set; } = null!;

        public string RequestingUserId { get; set; } = string.Empty;
        public string TargetUserId { get; set; } = string.Empty;

        public ConnectionStatus Status { get; set; } = ConnectionStatus.Requested;

        public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
        public DateTime? RespondedAt { get; set; }

        // Contact details are only shared once Status == Accepted.
    }
}
