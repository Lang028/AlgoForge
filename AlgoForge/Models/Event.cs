namespace AlgoForge.Models
{
    public class Event
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public DateTime EventDate { get; set; }
        // Optional: a small event can be run by the photographer alone, with no
        // organisation behind it. While this is null the creator holds the event outright;
        // it is filled in when they hand the event over (see OrganisationHandover).
        public Guid? OrganisationId { get; set; }
        public Organisation? Organisation { get; set; }

        // Org admin toggle: whether attendees must confirm a face tag before it's visible (D6)
        public bool TagConfirmationRequired { get; set; } = true;

        public EventStatus Status { get; set; } = EventStatus.Draft;

        public ICollection<EventMembership> Memberships { get; set; } = new List<EventMembership>();
        public ICollection<Attendee> Attendees { get; set; } = new List<Attendee>();
        public ICollection<Photo> Photos { get; set; } = new List<Photo>();
        public ICollection<PersonCluster> PersonClusters { get; set; } = new List<PersonCluster>();
        public ICollection<Album> Albums { get; set; } = new List<Album>();
    }
}
