namespace AlgoForge.Models
{
    // A cluster groups every FaceDetection the pipeline believes belongs
    // to the same person, across every photo in the event.
    public class FaceCluster
    {
        public int Id { get; set; }

        public int EventId { get; set; }
        public Event Event { get; set; } = null!;

        // Null until a Photographer identifies this cluster as a specific,
        // invited attendee (i.e. links it to an EventMembership).
        public int? IdentifiedEventMembershipId { get; set; }
        public EventMembership? IdentifiedEventMembership { get; set; }

        public string? IdentifiedByUserId { get; set; }
        public DateTime? IdentifiedAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<FaceDetection> FaceDetections { get; set; } = new List<FaceDetection>();

        public bool IsIdentified => IdentifiedEventMembershipId != null;
    }
}
