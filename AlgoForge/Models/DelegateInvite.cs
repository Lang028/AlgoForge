namespace AlgoForge.Models
{
    // D5: an attendee may nominate a small number of people to view the gallery on their
    // behalf -- a parent, a partner, someone who wasn't there. The delegate needs an
    // account of their own, so the invite is a token link like the attendee's: following
    // it while signed in creates the EventMembership(Delegate) that grants access.
    //
    // Scoped to one attendee AND one event on purpose. A delegate has no standing of their
    // own: revoke the attendee's nomination and the access goes with it.
    public class DelegateInvite
    {
        public Guid Id { get; set; }

        public Guid EventId { get; set; }
        public Event? Event { get; set; }

        /// <summary>The attendee doing the nominating -- their Attendee record for this event.</summary>
        public Guid AttendeeId { get; set; }
        public Attendee? Attendee { get; set; }

        /// <summary>The user account that nominated, recorded on the resulting membership.</summary>
        public Guid GrantedByUserId { get; set; }

        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;

        public string Token { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public Guid? ClaimedByUserId { get; set; }
        public ApplicationUser? ClaimedByUser { get; set; }

        /// <summary>Revoking withdraws the invite and any membership it created.</summary>
        public bool Revoked { get; set; }
    }
}
