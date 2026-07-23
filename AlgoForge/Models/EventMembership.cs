namespace AlgoForge.Models
{
    // The join entity that scopes a role to one event.
    // A user can hold multiple EventMemberships across different events/orgs.
    public class EventMembership
    {
        public int Id { get; set; }

        public string UserId { get; set; } = string.Empty;
        public ApplicationUser User { get; set; } = null!;

        public int EventId { get; set; }
        public Event Event { get; set; } = null!;

        public EventRole Role { get; set; }

        // Set when an Attendee grants delegate access to another user for this event.
        public string? DelegatingAttendeeUserId { get; set; }

        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    }
}
