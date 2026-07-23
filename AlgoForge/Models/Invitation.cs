using System.ComponentModel.DataAnnotations;

namespace AlgoForge.Models
{
    public class Invitation
    {
        public int Id { get; set; }

        public int EventId { get; set; }
        public Event Event { get; set; } = null!;

        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        public EventRole IntendedRole { get; set; }

        // Who sent the invite (Photographer inviting a Coordinator,
        // Coordinator inviting Attendees, or Attendee inviting a Delegate)
        public string InvitedByUserId { get; set; } = string.Empty;

        public string TokenHash { get; set; } = string.Empty;

        public InvitationStatus Status { get; set; } = InvitationStatus.Pending;

        public DateTime SentAt { get; set; } = DateTime.UtcNow;
        public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddDays(14);
    }
}
