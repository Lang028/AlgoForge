namespace AlgoForge.Models
{
    // D2: permission spine. Roles are per-event, not global -- authorization policies
    // read EventMembership.Role, never ASP.NET Identity's global roles.
    public class EventMembership
    {
        public Guid Id { get; set; }

        public Guid EventId { get; set; }
        public Event? Event { get; set; }

        public Guid UserId { get; set; }
        public ApplicationUser? User { get; set; }

        public EventRole Role { get; set; } = EventRole.Attendee;

        // Only set when Role == Delegate: which user granted this delegate access (D5).
        public Guid? GrantedByUserId { get; set; }
        public ApplicationUser? GrantedByUser { get; set; }

        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    }
}
