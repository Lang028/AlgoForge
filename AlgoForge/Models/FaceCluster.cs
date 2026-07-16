namespace AlgoForge.Models
{
    // Represents one person recurring across an event's photos.
    //
    // Clustering approach (v1, resolves OPEN-3/OPEN-4 for the first working slice):
    // incremental assignment only, never full re-cluster. A new face embedding is compared
    // (cosine similarity) against each existing cluster's RepresentativeEmbeddingRef for the
    // event; above threshold it joins that cluster, otherwise a new cluster is created.
    // Because clusters are never recomputed, an Id, once created, never changes -- so this
    // satisfies the "cluster IDs must stay stable" constraint by construction rather than by
    // tracking a version/migration scheme.
    public class FaceCluster
    {
        public Guid Id { get; set; }

        public Guid EventId { get; set; }
        public Event? Event { get; set; }

        public FaceClusterStatus Status { get; set; } = FaceClusterStatus.Unidentified;

        // Path to the representative embedding sidecar file (first detection's embedding).
        // Embeddings are never stored in SQL Server rows directly.
        public string RepresentativeEmbeddingRef { get; set; } = string.Empty;

        public Guid? LinkedAttendeeId { get; set; }
        public Attendee? LinkedAttendee { get; set; }

        public Guid? IdentifiedByUserId { get; set; }
        public ApplicationUser? IdentifiedByUser { get; set; }

        public ICollection<FaceDetection> Detections { get; set; } = new List<FaceDetection>();
    }
}
