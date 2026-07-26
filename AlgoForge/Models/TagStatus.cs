namespace AlgoForge.Models
{
    // D6: a tag starts Suggested and only becomes Confirmed by the tagged attendee's own
    // action (or auto-confirms on creation when Event.TagConfirmationRequired is off).
    // Rejected is a status flip, not a delete -- an auditable record that consent was
    // requested and declined.
    public enum TagStatus
    {
        Suggested,
        Confirmed,
        Rejected
    }
}
