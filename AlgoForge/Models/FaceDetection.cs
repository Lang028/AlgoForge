namespace AlgoForge.Models
{
    // One face found in one photo by the Python detection service.
    public class FaceDetection
    {
        public Guid Id { get; set; }

        public Guid PhotoId { get; set; }
        public Photo? Photo { get; set; }

        public Guid? FaceClusterId { get; set; }
        public FaceCluster? FaceCluster { get; set; }

        // Bounding box normalized to 0-1 relative to image width/height, so it renders
        // correctly at any display size without needing the original pixel dimensions.
        public double BoxX { get; set; }
        public double BoxY { get; set; }
        public double BoxWidth { get; set; }
        public double BoxHeight { get; set; }

        public double Confidence { get; set; }

        // Path to this detection's embedding sidecar file (not stored in SQL directly).
        public string EmbeddingRef { get; set; } = string.Empty;
    }
}
