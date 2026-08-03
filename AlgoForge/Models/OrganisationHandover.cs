namespace AlgoForge.Models
{
    // Handing an event over to an organisation.
    //
    // The photographer never creates the organisation themselves -- they invite an email
    // address, and whoever accepts creates the organisation and becomes its admin. That
    // keeps the organisation's own details (name, contact, who administers it) in the
    // hands of the people it belongs to, rather than guessed at by the photographer.
    //
    // Accepting links the organisation to the event and grants the accepter Coordinator on
    // it. The photographer keeps their own memberships: they still shot the event and
    // still need their gallery.
    public class OrganisationHandover
    {
        public Guid Id { get; set; }

        public Guid EventId { get; set; }
        public Event? Event { get; set; }

        public string Email { get; set; } = string.Empty;

        public string Token { get; set; } = string.Empty;

        public Guid InvitedByUserId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? AcceptedAt { get; set; }

        /// <summary>The organisation the accepter created; null until then.</summary>
        public Guid? CreatedOrganisationId { get; set; }

        /// <summary>Withdrawn by the photographer before it was accepted.</summary>
        public bool Revoked { get; set; }
    }
}
