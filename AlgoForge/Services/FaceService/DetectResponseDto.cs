using System.Text.Json.Serialization;

namespace AlgoForge.Services.FaceService
{
    public class DetectedFaceDto
    {
        [JsonPropertyName("box_x")]
        public double BoxX { get; set; }

        [JsonPropertyName("box_y")]
        public double BoxY { get; set; }

        [JsonPropertyName("box_width")]
        public double BoxWidth { get; set; }

        [JsonPropertyName("box_height")]
        public double BoxHeight { get; set; }

        [JsonPropertyName("confidence")]
        public double Confidence { get; set; }

        [JsonPropertyName("embedding")]
        public float[] Embedding { get; set; } = Array.Empty<float>();
    }

    public class DetectResponseDto
    {
        [JsonPropertyName("faces")]
        public List<DetectedFaceDto> Faces { get; set; } = new();
    }
}
