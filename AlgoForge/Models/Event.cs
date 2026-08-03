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

        // Set once, at creation, and deliberately offered nowhere afterwards.
        //
        // Switching a link-shared gallery to a consent one would mean running face
        // detection over photographs of people who were told none of that was happening,
        // which is the opposite of what this product promises. Switching the other way is
        // no better: it would strand tags and connections that attendees had already agreed
        // to, and expose to a link the very photographs they had approved for members only.
        public EventGalleryMode GalleryMode { get; set; } = EventGalleryMode.Consent;

        // The secret in a link-shared gallery's URL. Null on consent events, which have no
        // such link. Rotating it revokes every link handed out so far, and every browser
        // still holding the cookie one was exchanged for.
        public string? ShareToken { get; set; }

        public EventStatus Status { get; set; } = EventStatus.Draft;

        public ICollection<EventMembership> Memberships { get; set; } = new List<EventMembership>();
        public ICollection<Attendee> Attendees { get; set; } = new List<Attendee>();
        public ICollection<Photo> Photos { get; set; } = new List<Photo>();
        public ICollection<PersonCluster> PersonClusters { get; set; } = new List<PersonCluster>();
        public ICollection<Album> Albums { get; set; } = new List<Album>();
    }
}
