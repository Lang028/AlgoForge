
namespace AlgoForge.Models
{
    // An Attendee is an event-scoped person record created by whoever manages the
    // event (Photographer for now; Event Coordinator once that role exists) -- it
    // exists before the person has an account. The emailed invite link is how a
    // person claims their Attendee record with a real ApplicationUser account
    // (ClaimedByUserId). Tags against an unclaimed Attendee stay Suggested forever,
    // so consent is structurally enforced rather than assumed.
    //
    // Distinct from EventMembership: EventMembership only exists once a real user
    // account is linked to an event. Attendee exists first, and EventMembership
    // gets created at claim time (see the future ClaimController).
    public class Attendee
    {
        public int Id { get; set; }

        public int EventId { get; set; }
        public Event? Event { get; set; }

        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string ContactInfo { get; set; } = string.Empty;
        public string InviteToken { get; set; } = string.Empty;

        public string? ClaimedByUserId { get; set; }
        public ApplicationUser? ClaimedByUser { get; set; }

        // Attendee-controlled opt-in to show contact details without an accepted connection.
        public bool ContactsVisible { get; set; } = false;
    }
}
