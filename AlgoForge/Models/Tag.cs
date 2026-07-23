namespace AlgoForge.Models
{
    // Each FaceDetection carries its own Tag, so two people in the same
    // photo make fully independent consent decisions.
    public class Tag
    {
        public int Id { get; set; }

        public int FaceDetectionId { get; set; }
        public FaceDetection FaceDetection { get; set; } = null!;

        public string AttendeeUserId { get; set; } = string.Empty;

        public TagStatus Status { get; set; } = TagStatus.Suggested;

        // True if the attendee tagged themselves directly rather than the
        // system suggesting it via clustering.
        public bool IsSelfTagged { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Rejection flips status; it never deletes the row — this is the
        // auditable record that consent was requested and declined.
        public DateTime? RespondedAt { get; set; }
    }
}
