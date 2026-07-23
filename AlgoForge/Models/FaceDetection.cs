namespace AlgoForge.Models
{
    // One row per face found in a photo. SQL holds only the reference to the
    // embedding vector in blob storage, not the vector itself.
    public class FaceDetection
    {
        public int Id { get; set; }

        public int PhotoId { get; set; }
        public Photo Photo { get; set; } = null!;

        public int? FaceClusterId { get; set; }
        public FaceCluster? FaceCluster { get; set; }

        // Bounding box, stored as fractions of image width/height (0-1)
        public double BoundingBoxX { get; set; }
        public double BoundingBoxY { get; set; }
        public double BoundingBoxWidth { get; set; }
        public double BoundingBoxHeight { get; set; }

        public string EmbeddingBlobRef { get; set; } = string.Empty;

        // Set true only for faces linked to an invited attendee. Unlinked
        // faces (staff, passers-by) are never surfaced to attendees, and
        // their embeddings are purged on a schedule.
        public bool IsLinkedToAttendee => FaceCluster != null && FaceCluster.IsIdentified;

        public ICollection<Tag> Tags { get; set; } = new List<Tag>();
    }
}
