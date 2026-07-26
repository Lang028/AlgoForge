namespace AlgoForge.Models
{
    // One person found in one photo by the Python pipeline. Replaces FaceDetection:
    // a detection is now a whole person (box, appearance, hair) that may or may not have
    // a usable face, rather than a face that happens to belong to someone.
    //
    // The field that matters most here is IsTaggable. It is the Tier A / Tier B gate from
    // PERSON_MATCHING_PLAN.md 4.3, and it decides what any attendee can ever see: Tier B
    // detections are stored and may join a cluster as internal clustering evidence, but
    // they never carry a tag and never appear in an attendee-facing view.
    public class PersonDetection
    {
        public Guid Id { get; set; }

        public Guid PhotoId { get; set; }
        public Photo? Photo { get; set; }

        public Guid? PersonClusterId { get; set; }
        public PersonCluster? PersonCluster { get; set; }

        // Person box, normalized to 0-1 relative to image width/height so it renders
        // correctly over any rendition without needing the original pixel dimensions.
        public double BoxX { get; set; }
        public double BoxY { get; set; }
        public double BoxWidth { get; set; }
        public double BoxHeight { get; set; }

        // Face box, same coordinate space. Null when no face was found -- a backshot, or
        // someone turned away. The gallery draws this rather than the person box: a name
        // label belongs on a face, not on a full-body rectangle.
        public double? FaceX { get; set; }
        public double? FaceY { get; set; }
        public double? FaceWidth { get; set; }
        public double? FaceHeight { get; set; }

        // Detector confidence in the face specifically. 0 means no usable face. Drives
        // both the anchor threshold and the face veto during clustering.
        public double FaceQuality { get; set; }

        // Variance of the Laplacian over the person crop -- the focus proxy that separates
        // a subject from an out-of-focus figure in the background.
        public double Sharpness { get; set; }

        // Continuous 0-1 blend used only for ranking (best crop, review ordering).
        // Never for gating: that is IsTaggable's job and it is a hard pass/fail.
        public double ProminenceScore { get; set; }

        // THE GATE. False = Tier B = never taggable, never shown to an attendee.
        // Enforced in the service layer and in every attendee-facing query, not just in
        // views -- see PersonPipelineService and PhotosController.
        public bool IsTaggable { get; set; }

        // References into the Python service's sidecar store (D15). SQL never holds the
        // vectors themselves. Each may be null when that signal could not be computed.
        public string? FaceEmbeddingRef { get; set; }
        public string? AppearanceEmbeddingRef { get; set; }
        public string? HeadEmbeddingRef { get; set; }

        // The fused similarity that earned this detection its place in the cluster.
        // Anchors are 1.0. The review grid sorts ascending so the photographer checks the
        // shakiest attachments first.
        public double ClusterConfidence { get; set; }
    }
}
