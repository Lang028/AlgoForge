namespace AlgoForge.Models
{
    // Per-event role. A user's role is scoped to a single EventMembership,
    // so the same person can be Photographer on one event and Attendee on another.
    public enum EventRole
    {
        Photographer,
        EventCoordinator,
        Attendee,
        AttendeeDelegate
    }

    // Event lifecycle: Draft -> Live -> PostEvent -> Archived
    public enum EventStatus
    {
        Draft,
        Live,
        PostEvent,
        Archived
    }

    // Tag lifecycle for the consent model
    public enum TagStatus
    {
        Suggested,
        Confirmed,
        Rejected
    }

    // Whether an event auto-confirms identified tags (trusted/small events)
    // or requires the attendee to confirm each one (large events).
    public enum TagConfirmationMode
    {
        RequireConfirmation,
        AutoConfirm
    }

    public enum ConnectionStatus
    {
        Requested,
        Accepted,
        Declined
    }

    public enum InvitationStatus
    {
        Pending,
        Accepted,
        Expired
    }
}
