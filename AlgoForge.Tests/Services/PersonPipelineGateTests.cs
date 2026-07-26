using AlgoForge.Data;
using AlgoForge.Models;
using AlgoForge.Services.PersonPipeline;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AlgoForge.Tests.Services
{
    // The Tier A gate is a consent boundary, not a display preference, so it gets tests
    // rather than trust. PERSON_MATCHING_PLAN.md 9 lists "zero tags on Tier B detections"
    // as a v1 acceptance criterion; these are that criterion in executable form.
    //
    // Only SyncSuggestedTagsAsync is exercised -- it needs no HTTP -- because it is the
    // single place clustering is allowed to mint a tag.
    public class PersonPipelineGateTests
    {
        private static AlgoForgeDbContext NewContext() =>
            new(new DbContextOptionsBuilder<AlgoForgeDbContext>()
                .UseInMemoryDatabase($"gate-{Guid.NewGuid()}")
                .Options);

        private static PersonPipelineService NewService(AlgoForgeDbContext db) =>
            new(new HttpClient { BaseAddress = new Uri("http://localhost") },
                db,
                NullLogger<PersonPipelineService>.Instance);

        // Builds an event with one cluster whose detections have the given gate flags.
        private static async Task<(Guid EventId, Guid ClusterId, Guid AttendeeId, Guid UserId)> SeedAsync(
            AlgoForgeDbContext db, bool tagConfirmationRequired, params bool[] taggableFlags)
        {
            var eventId = Guid.NewGuid();
            var clusterId = Guid.NewGuid();
            var attendeeId = Guid.NewGuid();
            var userId = Guid.NewGuid();

            db.Events.Add(new Event
            {
                Id = eventId,
                Name = "Test Event",
                OrganisationId = Guid.NewGuid(),
                TagConfirmationRequired = tagConfirmationRequired
            });

            db.Attendees.Add(new Attendee
            {
                Id = attendeeId,
                EventId = eventId,
                Name = "Thandi",
                Email = "thandi@example.com"
            });

            db.PersonClusters.Add(new PersonCluster
            {
                Id = clusterId,
                EventId = eventId,
                Status = PersonClusterStatus.Unidentified,
                HasTaggableDetection = taggableFlags.Any(f => f)
            });

            // One photo per detection. Two boxes in a single photo are two different
            // people by construction, so a cluster containing several detections from the
            // same photo is a state the pipeline can never produce -- seeding one would
            // be testing against a fiction.
            foreach (var taggable in taggableFlags)
            {
                var photoId = Guid.NewGuid();
                db.Photos.Add(new Photo
                {
                    Id = photoId,
                    EventId = eventId,
                    UploadedByUserId = userId,
                    BlobUrl = $"/uploads/{photoId}.jpg"
                });

                db.PersonDetections.Add(new PersonDetection
                {
                    Id = Guid.NewGuid(),
                    PhotoId = photoId,
                    PersonClusterId = clusterId,
                    IsTaggable = taggable,
                    FaceQuality = taggable ? 0.9 : 0.0
                });
            }

            await db.SaveChangesAsync();
            return (eventId, clusterId, attendeeId, userId);
        }

        [Fact]
        public async Task SyncSuggestedTags_TagsOnlyTaggableDetections()
        {
            using var db = NewContext();
            // Three clearly visible, two Tier B (a backshot and a blurry passer-by).
            var (eventId, clusterId, attendeeId, userId) =
                await SeedAsync(db, true, true, true, true, false, false);

            var created = await NewService(db).SyncSuggestedTagsAsync(eventId, clusterId, attendeeId, userId);

            Assert.Equal(3, created);

            var tags = await db.Tags.ToListAsync();
            var taggableIds = await db.PersonDetections
                .Where(d => d.IsTaggable).Select(d => d.Id).ToListAsync();

            Assert.Equal(3, tags.Count);
            Assert.All(tags, t => Assert.Contains(t.PersonDetectionId, taggableIds));
        }

        [Fact]
        public async Task SyncSuggestedTags_TierBDetectionNeverGetsATag()
        {
            using var db = NewContext();
            var (eventId, clusterId, attendeeId, userId) = await SeedAsync(db, true, true, false);

            await NewService(db).SyncSuggestedTagsAsync(eventId, clusterId, attendeeId, userId);

            var tierBId = await db.PersonDetections
                .Where(d => !d.IsTaggable).Select(d => d.Id).SingleAsync();

            Assert.DoesNotContain(await db.Tags.ToListAsync(), t => t.PersonDetectionId == tierBId);
        }

        [Fact]
        public async Task SyncSuggestedTags_ClusterWithNoTaggableDetections_CreatesNothing()
        {
            using var db = NewContext();
            // The person who avoided the camera all day. Identifying this cluster must be
            // a no-op even if a photographer somehow reaches the action.
            var (eventId, clusterId, attendeeId, userId) = await SeedAsync(db, true, false, false, false);

            var created = await NewService(db).SyncSuggestedTagsAsync(eventId, clusterId, attendeeId, userId);

            Assert.Equal(0, created);
            Assert.Empty(await db.Tags.ToListAsync());
        }

        [Fact]
        public async Task SyncSuggestedTags_DefaultsToSuggestedAwaitingConsent()
        {
            using var db = NewContext();
            var (eventId, clusterId, attendeeId, userId) = await SeedAsync(db, true, true);

            await NewService(db).SyncSuggestedTagsAsync(eventId, clusterId, attendeeId, userId);

            var tag = await db.Tags.SingleAsync();
            Assert.Equal(TagStatus.Suggested, tag.Status);
            Assert.Equal(TagOrigin.ClusterMatch, tag.Origin);
            Assert.Null(tag.ResolvedAt);
        }

        [Fact]
        public async Task SyncSuggestedTags_AutoConfirmsWhenEventDoesNotRequireConfirmation()
        {
            using var db = NewContext();
            // D6: small, trusted events can switch confirmation off.
            var (eventId, clusterId, attendeeId, userId) = await SeedAsync(db, false, true);

            await NewService(db).SyncSuggestedTagsAsync(eventId, clusterId, attendeeId, userId);

            var tag = await db.Tags.SingleAsync();
            Assert.Equal(TagStatus.Confirmed, tag.Status);
            Assert.NotNull(tag.ResolvedAt);
        }

        [Fact]
        public async Task SyncSuggestedTags_IsIdempotent()
        {
            using var db = NewContext();
            // Runs after every recluster, so duplicate tags would pile up fast -- and each
            // one would be a separate consent decision the attendee has to dismiss.
            var (eventId, clusterId, attendeeId, userId) = await SeedAsync(db, true, true, true);
            var service = NewService(db);

            var first = await service.SyncSuggestedTagsAsync(eventId, clusterId, attendeeId, userId);
            var second = await service.SyncSuggestedTagsAsync(eventId, clusterId, attendeeId, userId);

            Assert.Equal(2, first);
            Assert.Equal(0, second);
            Assert.Equal(2, await db.Tags.CountAsync());
        }

        [Fact]
        public async Task SyncSuggestedTags_DoesNotResurrectARejectedTag()
        {
            using var db = NewContext();
            var (eventId, clusterId, attendeeId, userId) = await SeedAsync(db, true, true);
            var service = NewService(db);

            await service.SyncSuggestedTagsAsync(eventId, clusterId, attendeeId, userId);

            // The attendee says no. D6: rejection is a status flip, not a delete.
            var tag = await db.Tags.SingleAsync();
            tag.Status = TagStatus.Rejected;
            tag.ResolvedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            // A later recluster must not quietly re-suggest what they already declined.
            await service.SyncSuggestedTagsAsync(eventId, clusterId, attendeeId, userId);

            Assert.Equal(1, await db.Tags.CountAsync());
            Assert.Equal(TagStatus.Rejected, (await db.Tags.SingleAsync()).Status);
        }
    }
}
