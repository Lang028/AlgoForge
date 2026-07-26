using System.Text.Json.Serialization;

namespace AlgoForge.Services.PersonPipeline
{
    // Wire contract with the Python service (face_service/pipeline/schemas.py).
    // Python stays snake_case and C# stays PascalCase; the mapping is explicit here so
    // neither side has to adopt the other's naming convention.

    public class BoxDto
    {
        [JsonPropertyName("x")]
        public double X { get; set; }

        [JsonPropertyName("y")]
        public double Y { get; set; }

        [JsonPropertyName("w")]
        public double W { get; set; }

        [JsonPropertyName("h")]
        public double H { get; set; }
    }

    public class DetectionDto
    {
        // Minted by the Python service (D14). The embedding files are already written
        // under this id by the time we see it, so it becomes the row's primary key rather
        // than something we generate here.
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("box")]
        public BoxDto Box { get; set; } = new();

        [JsonPropertyName("face_box")]
        public BoxDto? FaceBox { get; set; }

        [JsonPropertyName("face_quality")]
        public double FaceQuality { get; set; }

        [JsonPropertyName("sharpness")]
        public double Sharpness { get; set; }

        [JsonPropertyName("prominence")]
        public double Prominence { get; set; }

        [JsonPropertyName("is_taggable")]
        public bool IsTaggable { get; set; }

        [JsonPropertyName("face_embedding_ref")]
        public string? FaceEmbeddingRef { get; set; }

        [JsonPropertyName("appearance_embedding_ref")]
        public string? AppearanceEmbeddingRef { get; set; }

        [JsonPropertyName("head_embedding_ref")]
        public string? HeadEmbeddingRef { get; set; }
    }

    public class DetectResponseDto
    {
        [JsonPropertyName("photo_id")]
        public string PhotoId { get; set; } = string.Empty;

        [JsonPropertyName("detections")]
        public List<DetectionDto> Detections { get; set; } = new();

        // Which optional models were live for this run. Logged, not stored: it separates
        // "no appearance signal because the crop was unusable" from "no appearance signal
        // because torchreid isn't installed on this machine".
        [JsonPropertyName("signals")]
        public Dictionary<string, object>? Signals { get; set; }
    }

    public class ClusterInputDto
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("photo_id")]
        public string PhotoId { get; set; } = string.Empty;

        [JsonPropertyName("face_quality")]
        public double FaceQuality { get; set; }

        [JsonPropertyName("is_taggable")]
        public bool IsTaggable { get; set; }

        [JsonPropertyName("face_embedding_ref")]
        public string? FaceEmbeddingRef { get; set; }

        [JsonPropertyName("appearance_embedding_ref")]
        public string? AppearanceEmbeddingRef { get; set; }

        [JsonPropertyName("head_embedding_ref")]
        public string? HeadEmbeddingRef { get; set; }
    }

    public class ConstraintDto
    {
        [JsonPropertyName("a")]
        public string A { get; set; } = string.Empty;

        [JsonPropertyName("b")]
        public string B { get; set; } = string.Empty;

        // "must_link" | "cannot_link"
        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;
    }

    public class ClusterRequestDto
    {
        [JsonPropertyName("event_id")]
        public string EventId { get; set; } = string.Empty;

        [JsonPropertyName("detections")]
        public List<ClusterInputDto> Detections { get; set; } = new();

        // Photographer corrections, replayed as hard constraints. Always empty until M4
        // builds the merge/split tools; the field exists now so adding them later does
        // not change the wire format.
        [JsonPropertyName("constraints")]
        public List<ConstraintDto> Constraints { get; set; } = new();
    }

    public class ClusterMemberDto
    {
        [JsonPropertyName("detection_id")]
        public string DetectionId { get; set; } = string.Empty;

        [JsonPropertyName("confidence")]
        public double Confidence { get; set; }
    }

    public class ClusterResultDto
    {
        [JsonPropertyName("members")]
        public List<ClusterMemberDto> Members { get; set; } = new();

        [JsonPropertyName("anchor_detection_id")]
        public string? AnchorDetectionId { get; set; }

        [JsonPropertyName("has_taggable")]
        public bool HasTaggable { get; set; }
    }

    public class ClusterResponseDto
    {
        [JsonPropertyName("event_id")]
        public string EventId { get; set; } = string.Empty;

        [JsonPropertyName("clusters")]
        public List<ClusterResultDto> Clusters { get; set; } = new();

        [JsonPropertyName("unclustered")]
        public List<string> Unclustered { get; set; } = new();
    }
}
