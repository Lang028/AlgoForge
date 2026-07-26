namespace AlgoForge.ViewModels.Photos
{
    // What the viewer is allowed to know about one face in one photo. Built server-side so
    // contact details are simply absent from the payload unless the viewer has earned them
    // (D11) -- there is no client-side hiding of data the browser was still sent.
    public enum FaceConnectionState
    {
        /// The viewer is looking at their own face.
        Self,

        /// Tagged and confirmed, but this person hasn't claimed their invite yet, so there
        /// is no account to connect to (D10).
        NoAccount,

        /// Connectable, nothing sent yet.
        Open,

        /// The viewer has sent a request and is waiting.
        Sent,

        /// The other person asked first -- the viewer can accept or decline right here.
        AwaitingYou,

        /// Accepted by both sides: contact details are shared.
        Connected,

        /// Asked and declined. Not re-askable.
        Declined
    }

    public class GalleryFace
    {
        public Guid DetectionId { get; set; }
        public Guid AttendeeId { get; set; }
        public string Name { get; set; } = string.Empty;

        // Normalised 0-1 box, so it overlays correctly at any rendered size.
        public double BoxX { get; set; }
        public double BoxY { get; set; }
        public double BoxWidth { get; set; }
        public double BoxHeight { get; set; }

        public FaceConnectionState State { get; set; }
        public Guid? ConnectionId { get; set; }

        // Populated only when State is Connected or Self, or the attendee opted into
        // making contacts visible to event members (D11). Null otherwise, always.
        public string? ContactEmail { get; set; }
        public string? ContactInfo { get; set; }
        public bool ContactsSharedByOptIn { get; set; }
    }

    public class GalleryPhoto
    {
        public Guid Id { get; set; }
        public string Url { get; set; } = string.Empty;
        public DateTime UploadedAt { get; set; }
        public List<GalleryFace> Faces { get; set; } = new();
    }

    public class GalleryViewModel
    {
        public Guid EventId { get; set; }
        public string EventName { get; set; } = string.Empty;
        public List<GalleryPhoto> Photos { get; set; } = new();

        /// Distinct people who have confirmed a tag in this event.
        public int ConfirmedPeopleCount { get; set; }

        /// Faces detected but not surfaced -- unidentified, or tagged and awaiting consent.
        /// Shown as a count only, never as boxes on the photos.
        public int AwaitingConsentCount { get; set; }
    }
}
