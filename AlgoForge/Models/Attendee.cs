namespace AlgoForge.Models
{
    // D3: an Attendee is an event-scoped person record created from the coordinator's
    // uploaded invitee list -- it exists before the person has an account. Clusters are
    // linked and tags are suggested against the Attendee record. The emailed invite link
    // is how a person claims their Attendee record with a User account (ClaimedByUserId).
    // Tags on an unclaimed Attendee stay Suggested forever, so consent is structurally enforced.
    public class Attendee
    {
        public Guid Id { get; set; }

        public Guid EventId { get; set; }
        public Event? Event { get; set; }

        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string ContactInfo { get; set; } = string.Empty;
        public string InviteToken { get; set; } = string.Empty;

        /// <summary>When the invite email was last successfully sent; null means never invited.
        /// Together with ClaimedByUserId this drives the status: not invited -> invite sent -> invite accepted.</summary>
        public DateTime? InviteSentAt { get; set; }

        public Guid? ClaimedByUserId { get; set; }
        public ApplicationUser? ClaimedByUser { get; set; }

        // D11: attendee-controlled opt-in to show contact details without an accepted connection.
        public bool ContactsVisible { get; set; } = false;

        /// <summary>What the attendee does -- their role, company, or a line about themselves.
        /// Written by the attendee, shown to other members of the same event beside their name.
        /// Unlike ContactInfo this is not a way to reach them, so it needs no connection.</summary>
        public string About { get; set; } = string.Empty;
    }
}
