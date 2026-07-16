namespace AlgoForge.Models
{
    public class Event
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public DateTime EventDate { get; set; }
        public Guid OrganisationId { get; set; }
        public Organisation? Organisation { get; set; }
       

        // Org admin toggle: whether attendees must confirm a face tag before it's visible (D6)
        public bool TagConfirmationRequired { get; set; } = true;

        public EventStatus Status { get; set; } = EventStatus.Draft;

        public ICollection<EventMembership> Memberships { get; set; } = new List<EventMembership>();
        public ICollection<Attendee> Attendees { get; set; } = new List<Attendee>();
        public ICollection<Photo> Photos { get; set; } = new List<Photo>();
        public ICollection<FaceCluster> FaceClusters { get; set; } = new List<FaceCluster>();
        public ICollection<Album> Albums { get; set; } = new List<Album>();
    }
}
