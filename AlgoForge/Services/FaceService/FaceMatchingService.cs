using System.Net.Http.Json;
using System.Text.Json;
using AlgoForge.Data;
using AlgoForge.Models;
using Microsoft.EntityFrameworkCore;

namespace AlgoForge.Services.FaceService
{
    // Calls the Python detection service, then does clustering + persistence here in C#
    // (v1 simplification -- see FaceCluster.cs for the OPEN-3/OPEN-4 reasoning).
    public class FaceMatchingService
    {
        // ArcFace/InsightFace normed embeddings: ~0.5+ cosine similarity reliably means
        // "same person" in practice: this is a starting point, not a tuned constant.
        private const double SimilarityThreshold = 0.50;

        private readonly HttpClient _httpClient;
        private readonly AlgoForgeDbContext _db;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<FaceMatchingService> _logger;

        public FaceMatchingService(HttpClient httpClient, AlgoForgeDbContext db, IWebHostEnvironment env, ILogger<FaceMatchingService> logger)
        {
            _httpClient = httpClient;
            _db = db;
            _env = env;
            _logger = logger;
        }

        public async Task ProcessPhotoAsync(Photo photo, string absoluteFilePath)
        {
            try
            {
                var result = await CallDetectAsync(absoluteFilePath);
                if (result is null)
                {
                    photo.FaceProcessingStatus = PhotoFaceProcessingStatus.Failed;
                    return;
                }

                var existingClusters = await _db.FaceClusters
                    .Where(c => c.EventId == photo.EventId)
                    .ToListAsync();

                var clusterEmbeddings = new Dictionary<Guid, float[]>();
                foreach (var cluster in existingClusters)
                {
                    clusterEmbeddings[cluster.Id] = await LoadEmbeddingAsync(cluster.RepresentativeEmbeddingRef);
                }

                foreach (var face in result.Faces)
                {
                    var detectionId = Guid.NewGuid();
                    var embeddingRef = await SaveEmbeddingAsync(detectionId, face.Embedding);

                    var matchedClusterId = FindBestCluster(face.Embedding, clusterEmbeddings);

                    if (matchedClusterId is null)
                    {
                        var newCluster = new FaceCluster
                        {
                            Id = Guid.NewGuid(),
                            EventId = photo.EventId,
                            RepresentativeEmbeddingRef = embeddingRef
                        };
                        _db.FaceClusters.Add(newCluster);
                        clusterEmbeddings[newCluster.Id] = face.Embedding;
                        matchedClusterId = newCluster.Id;
                    }

                    _db.FaceDetections.Add(new FaceDetection
                    {
                        Id = detectionId,
                        PhotoId = photo.Id,
                        FaceClusterId = matchedClusterId,
                        BoxX = face.BoxX,
                        BoxY = face.BoxY,
                        BoxWidth = face.BoxWidth,
                        BoxHeight = face.BoxHeight,
                        Confidence = face.Confidence,
                        EmbeddingRef = embeddingRef
                    });
                }

                photo.FaceProcessingStatus = PhotoFaceProcessingStatus.Processed;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Face detection failed for photo {PhotoId}", photo.Id);
                photo.FaceProcessingStatus = PhotoFaceProcessingStatus.Failed;
            }
        }

        private async Task<DetectResponseDto?> CallDetectAsync(string absoluteFilePath)
        {
            using var content = new MultipartFormDataContent();
            var bytes = await File.ReadAllBytesAsync(absoluteFilePath);
            var byteContent = new ByteArrayContent(bytes);
            byteContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
            content.Add(byteContent, "file", Path.GetFileName(absoluteFilePath));

            var response = await _httpClient.PostAsync("/detect", content);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<DetectResponseDto>();
        }

        private static Guid? FindBestCluster(float[] embedding, Dictionary<Guid, float[]> clusterEmbeddings)
        {
            Guid? bestId = null;
            double bestSimilarity = SimilarityThreshold;

            foreach (var (clusterId, clusterEmbedding) in clusterEmbeddings)
            {
                var similarity = CosineSimilarity(embedding, clusterEmbedding);
                if (similarity > bestSimilarity)
                {
                    bestSimilarity = similarity;
                    bestId = clusterId;
                }
            }

            return bestId;
        }

        private static double CosineSimilarity(float[] a, float[] b)
        {
            if (a.Length != b.Length || a.Length == 0)
            {
                return 0;
            }

            double dot = 0, magA = 0, magB = 0;
            for (var i = 0; i < a.Length; i++)
            {
                dot += a[i] * b[i];
                magA += a[i] * a[i];
                magB += b[i] * b[i];
            }

            if (magA == 0 || magB == 0)
            {
                return 0;
            }

            return dot / (Math.Sqrt(magA) * Math.Sqrt(magB));
        }

        private string EmbeddingsDirectory => Path.Combine(_env.ContentRootPath, "App_Data", "embeddings");

        private async Task<string> SaveEmbeddingAsync(Guid id, float[] embedding)
        {
            Directory.CreateDirectory(EmbeddingsDirectory);
            var relativeRef = Path.Combine("App_Data", "embeddings", $"{id}.json");
            var fullPath = Path.Combine(_env.ContentRootPath, relativeRef);
            await File.WriteAllTextAsync(fullPath, JsonSerializer.Serialize(embedding));
            return relativeRef;
        }

        private async Task<float[]> LoadEmbeddingAsync(string relativeRef)
        {
            var fullPath = Path.Combine(_env.ContentRootPath, relativeRef);
            if (!File.Exists(fullPath))
            {
                return Array.Empty<float>();
            }

            var json = await File.ReadAllTextAsync(fullPath);
            return JsonSerializer.Deserialize<float[]>(json) ?? Array.Empty<float>();
        }
    }
}
