using System.Net.Http.Json;
using AlgoForge.Data;
using AlgoForge.Models;
using Microsoft.EntityFrameworkCore;

namespace AlgoForge.Services.PersonPipeline
{
    // The web app's half of the person identification pipeline
    // (docs/PERSON_MATCHING_PLAN.md, milestones M1-M3).
    //
    // Division of labour: Python sees pixels and decides what is there; this class
    // decides what that is allowed to mean. Every consent rule lives on this side --
    // the pipeline reports detections and groupings, and never knows an attendee exists.
    //
    // Transport note: /detect is currently called inline from the upload request. D16
    // says face processing must never be synchronous, and at ~1-3s per photo a 300-photo
    // batch will time out long before it finishes. The two calls into Python are
    // deliberately the only I/O boundary in this class, so moving them behind a queue is
    // a change to the call sites in PhotosController, not to any logic here.
    public class PersonPipelineService
    {
        private readonly HttpClient _httpClient;
        private readonly AlgoForgeDbContext _db;
        private readonly ILogger<PersonPipelineService> _logger;

        public PersonPipelineService(
            HttpClient httpClient,
            AlgoForgeDbContext db,
            ILogger<PersonPipelineService> logger)
        {
            _httpClient = httpClient;
            _db = db;
            _logger = logger;
        }

        // --- Per-photo processing (plan 4) ---------------------------------------

        // Detects people in one photo and stores a PersonDetection per person. Does not
        // cluster: clustering is per-event and runs once per batch, not once per photo.
        public async Task<bool> ProcessPhotoAsync(Photo photo, string absoluteFilePath)
        {
            try
            {
                var result = await CallDetectAsync(photo, absoluteFilePath);
                if (result is null)
                {
                    photo.FaceProcessingStatus = PhotoFaceProcessingStatus.Failed;
                    return false;
                }

                foreach (var detection in result.Detections)
                {
                    if (!Guid.TryParse(detection.Id, out var detectionId))
                    {
                        _logger.LogWarning(
                            "Pipeline returned an unparseable detection id {Id} for photo {PhotoId}; skipping.",
                            detection.Id, photo.Id);
                        continue;
                    }

                    _db.PersonDetections.Add(new PersonDetection
                    {
                        Id = detectionId,
                        PhotoId = photo.Id,
                        BoxX = detection.Box.X,
                        BoxY = detection.Box.Y,
                        BoxWidth = detection.Box.W,
                        BoxHeight = detection.Box.H,
                        FaceX = detection.FaceBox?.X,
                        FaceY = detection.FaceBox?.Y,
                        FaceWidth = detection.FaceBox?.W,
                        FaceHeight = detection.FaceBox?.H,
                        FaceQuality = detection.FaceQuality,
                        Sharpness = detection.Sharpness,
                        ProminenceScore = detection.Prominence,
                        IsTaggable = detection.IsTaggable,
                        FaceEmbeddingRef = detection.FaceEmbeddingRef,
                        AppearanceEmbeddingRef = detection.AppearanceEmbeddingRef,
                        HeadEmbeddingRef = detection.HeadEmbeddingRef
                    });
                }

                _logger.LogInformation(
                    "Photo {PhotoId}: {Total} detections, {Taggable} taggable.",
                    photo.Id, result.Detections.Count, result.Detections.Count(d => d.IsTaggable));

                photo.FaceProcessingStatus = PhotoFaceProcessingStatus.Processed;
                return true;
            }
            catch (Exception ex)
            {
                // OPEN-5, still unresolved: without a queue there is no retry or
                // dead-letter, so a failure is recorded on the row and the photo is
                // simply reprocessable later. It must not take the upload down with it.
                _logger.LogError(ex, "Person detection failed for photo {PhotoId}", photo.Id);
                photo.FaceProcessingStatus = PhotoFaceProcessingStatus.Failed;
                return false;
            }
        }

        private async Task<DetectResponseDto?> CallDetectAsync(Photo photo, string absoluteFilePath)
        {
            using var content = new MultipartFormDataContent();

            var bytes = await File.ReadAllBytesAsync(absoluteFilePath);
            var fileContent = new ByteArrayContent(bytes);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
            content.Add(fileContent, "file", Path.GetFileName(absoluteFilePath));

            // The event id scopes the embedding sidecar store, so the worker can purge a
            // whole event's vectors as one directory when it is archived (D7).
            content.Add(new StringContent(photo.Id.ToString()), "photo_id");
            content.Add(new StringContent(photo.EventId.ToString()), "event_id");

            var response = await _httpClient.PostAsync("/detect", content);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<DetectResponseDto>();
        }

        // --- Clustering (plan 5) --------------------------------------------------

        // Reclusters an entire event and reconciles the result onto existing rows.
        //
        // The clustering itself is stateless and starts from scratch every time, which is
        // what plan 5 asks for. The delicate part is the reconciliation below, because a
        // cluster that a coordinator has already identified must come out the other side
        // with the same Id (OPEN-4) -- otherwise the identification work is lost and,
        // worse, tags end up attached to a person nobody named.
        public async Task<int> ReclusterEventAsync(Guid eventId)
        {
            var detections = await _db.PersonDetections
                .Where(d => d.Photo!.EventId == eventId)
                .ToListAsync();

            if (detections.Count == 0)
            {
                return 0;
            }

            var request = new ClusterRequestDto
            {
                EventId = eventId.ToString(),
                Detections = detections.Select(d => new ClusterInputDto
                {
                    Id = d.Id.ToString(),
                    PhotoId = d.PhotoId.ToString(),
                    FaceQuality = d.FaceQuality,
                    IsTaggable = d.IsTaggable,
                    FaceEmbeddingRef = d.FaceEmbeddingRef,
                    AppearanceEmbeddingRef = d.AppearanceEmbeddingRef,
                    HeadEmbeddingRef = d.HeadEmbeddingRef
                }).ToList()
            };

            var response = await _httpClient.PostAsJsonAsync("/cluster", request);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<ClusterResponseDto>();
            if (result is null)
            {
                return 0;
            }

            var existingClusters = await _db.PersonClusters
                .Where(c => c.EventId == eventId)
                .Include(c => c.Detections)
                .ToListAsync();

            await ReconcileAsync(eventId, existingClusters, result, detections);
            return result.Clusters.Count;
        }

        // Maps freshly computed groups onto existing cluster rows, preserving the Id of
        // any cluster a human has already acted on.
        //
        // Strategy: greedy best-overlap matching, strongest pairing first. A group and a
        // previously-identified cluster that share the most detections are the same
        // person, so the group inherits that cluster's row -- and with it the attendee
        // link, the identifier, and the meaning of every tag already suggested from it.
        private async Task ReconcileAsync(
            Guid eventId,
            List<PersonCluster> existingClusters,
            ClusterResponseDto result,
            List<PersonDetection> detections)
        {
            var detectionsById = detections.ToDictionary(d => d.Id);

            // Only clusters a human has touched need a stable identity. Unidentified ones
            // carry no decisions, so rebuilding them freely is both safe and desirable --
            // that is how a late upload gets to correct an earlier mis-grouping.
            var anchored = existingClusters
                .Where(c => c.Status != PersonClusterStatus.Unidentified)
                .ToList();

            var groups = result.Clusters
                .Select(cluster => new ComputedGroup(
                    cluster,
                    cluster.Members
                        .Select(m => Guid.TryParse(m.DetectionId, out var id) ? id : Guid.Empty)
                        .Where(id => id != Guid.Empty && detectionsById.ContainsKey(id))
                        .ToHashSet()))
                .ToList();

            // Score every (group, anchored cluster) pair, then take them strongest-first
            // so the best evidence claims its row before a weaker overlap can steal it.
            var overlaps =
                (from grp in groups
                 from cluster in anchored
                 let shared = cluster.Detections.Count(d => grp.DetectionIds.Contains(d.Id))
                 where shared > 0
                 orderby shared descending
                 select new { Group = grp, Cluster = cluster }).ToList();

            var claimedClusters = new HashSet<Guid>();

            foreach (var overlap in overlaps)
            {
                if (overlap.Group.Matched is not null || claimedClusters.Contains(overlap.Cluster.Id))
                {
                    continue;
                }

                overlap.Group.Matched = overlap.Cluster;
                claimedClusters.Add(overlap.Cluster.Id);
            }

            // Detach everything first, so a detection that moved between clusters does not
            // briefly belong to both.
            foreach (var detection in detections)
            {
                detection.PersonClusterId = null;
                detection.ClusterConfidence = 0;
            }

            foreach (var grp in groups)
            {
                var cluster = grp.Matched;
                if (cluster is null)
                {
                    cluster = new PersonCluster
                    {
                        Id = Guid.NewGuid(),
                        EventId = eventId,
                        Status = PersonClusterStatus.Unidentified
                    };
                    _db.PersonClusters.Add(cluster);
                }

                cluster.HasTaggableDetection = grp.Source.HasTaggable;
                cluster.AnchorDetectionId =
                    Guid.TryParse(grp.Source.AnchorDetectionId, out var anchorId) ? anchorId : null;

                foreach (var member in grp.Source.Members)
                {
                    if (Guid.TryParse(member.DetectionId, out var detectionId)
                        && detectionsById.TryGetValue(detectionId, out var detection))
                    {
                        detection.PersonClusterId = cluster.Id;
                        detection.ClusterConfidence = member.Confidence;
                    }
                }
            }

            // Unidentified clusters that no group claimed are stale groupings with nothing
            // hanging off them -- delete. Identified ones are kept even when empty: the
            // row records that a human said "this is Thandi", and that judgement should
            // outlive a reshuffle rather than being silently discarded.
            var stale = existingClusters
                .Where(c => c.Status == PersonClusterStatus.Unidentified && !claimedClusters.Contains(c.Id))
                .ToList();
            _db.PersonClusters.RemoveRange(stale);

            await _db.SaveChangesAsync();

            // An identified cluster may have picked up detections from the new photos.
            // D9: that must cascade suggested tags, or uploading more photos of someone
            // already identified would quietly do nothing.
            foreach (var cluster in existingClusters.Where(c => c.Status == PersonClusterStatus.Identified))
            {
                if (cluster.LinkedAttendeeId is not null && cluster.IdentifiedByUserId is not null)
                {
                    await SyncSuggestedTagsAsync(
                        eventId, cluster.Id, cluster.LinkedAttendeeId.Value, cluster.IdentifiedByUserId.Value);
                }
            }

            _logger.LogInformation(
                "Event {EventId}: {Clusters} clusters ({Surfaced} surfaced), {Unclustered} unclustered, {Reused} identities preserved.",
                eventId, result.Clusters.Count, result.Clusters.Count(c => c.HasTaggable),
                result.Unclustered.Count, claimedClusters.Count);
        }

        // One freshly computed group, plus the existing cluster row it was matched to
        // (null until matching claims one, and for genuinely new people).
        private sealed class ComputedGroup
        {
            public ComputedGroup(ClusterResultDto source, HashSet<Guid> detectionIds)
            {
                Source = source;
                DetectionIds = detectionIds;
            }

            public ClusterResultDto Source { get; }
            public HashSet<Guid> DetectionIds { get; }
            public PersonCluster? Matched { get; set; }
        }

        // --- Tagging (plan 6) -----------------------------------------------------

        // Creates Suggested tags for every Tier A detection in an identified cluster.
        //
        // This is the single place tags are minted from clustering, and it is where the
        // gate is enforced rather than merely displayed: a tag may only ever exist on a
        // detection with IsTaggable = true. Someone who is a blurry shape in the
        // background of a photo is not a consent decision anyone gets to make -- not the
        // coordinator's, and not their own.
        public async Task<int> SyncSuggestedTagsAsync(
            Guid eventId, Guid clusterId, Guid attendeeId, Guid actingUserId)
        {
            var evt = await _db.Events.FindAsync(eventId);
            if (evt is null)
            {
                return 0;
            }

            var taggableDetectionIds = await _db.PersonDetections
                .Where(d => d.PersonClusterId == clusterId && d.IsTaggable)
                .Select(d => d.Id)
                .ToListAsync();

            if (taggableDetectionIds.Count == 0)
            {
                return 0;
            }

            // Scoped to this attendee: a detection may legitimately carry a rejected tag
            // for one person and a suggested tag for another after a correction.
            var alreadyTagged = await _db.Tags
                .Where(t => t.TaggedAttendeeId == attendeeId && taggableDetectionIds.Contains(t.PersonDetectionId))
                .Select(t => t.PersonDetectionId)
                .ToListAsync();

            // D6: where the event has confirmation switched off (small, trusted events),
            // identified tags land Confirmed. Everywhere else they wait on the attendee.
            var initialStatus = evt.TagConfirmationRequired ? TagStatus.Suggested : TagStatus.Confirmed;
            var created = 0;

            foreach (var detectionId in taggableDetectionIds.Except(alreadyTagged))
            {
                _db.Tags.Add(new Tag
                {
                    Id = Guid.NewGuid(),
                    PersonDetectionId = detectionId,
                    TaggedAttendeeId = attendeeId,
                    Status = initialStatus,
                    Origin = TagOrigin.ClusterMatch,
                    CreatedByUserId = actingUserId,
                    CreatedAt = DateTime.UtcNow,
                    ResolvedAt = initialStatus == TagStatus.Confirmed ? DateTime.UtcNow : null
                });
                created++;
            }

            if (created > 0)
            {
                await _db.SaveChangesAsync();
            }

            return created;
        }
    }
}
