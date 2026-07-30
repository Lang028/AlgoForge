using AlgoForge.Data;
using AlgoForge.Models;
using Microsoft.EntityFrameworkCore;

namespace AlgoForge.Services.PersonPipeline
{
    // Drains PhotoDetectionQueue one photo at a time. Deliberately sequential: the Python
    // service is one process on one machine (D19 -- "the worker runs on a team laptop"), so
    // running detections concurrently would just contend for the same CPU instead of
    // finishing sooner.
    //
    // When the last pending photo in an event finishes, this also triggers that event's
    // clustering pass. That used to be the job of FinishUpload / RetryDetection, run once
    // synchronously right after their own batch -- which worked only because detection was
    // synchronous too, so "my batch is done" and "the event is done" were the same moment.
    // Once detection moved to a queue those two are no longer the same moment (a different
    // batch, or a different photographer entirely, could still have work in flight), so the
    // trigger has to live here, on whichever request actually empties the queue.
    public class PhotoDetectionWorker : BackgroundService
    {
        private readonly PhotoDetectionQueue _queue;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<PhotoDetectionWorker> _logger;

        public PhotoDetectionWorker(
            PhotoDetectionQueue queue,
            IServiceScopeFactory scopeFactory,
            ILogger<PhotoDetectionWorker> logger)
        {
            _queue = queue;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await foreach (var photoId in _queue.DequeueAllAsync(stoppingToken))
            {
                try
                {
                    await ProcessOneAsync(photoId, stoppingToken);
                }
                catch (Exception ex)
                {
                    // Must not take the worker loop down -- a bad photo or a transient DB
                    // hiccup here would otherwise silently stop every future upload from
                    // ever being detected until the app restarts.
                    _logger.LogError(ex, "Background face detection crashed for photo {PhotoId}.", photoId);
                }
            }
        }

        private async Task ProcessOneAsync(Guid photoId, CancellationToken stoppingToken)
        {
            // A fresh scope per photo: this loop can run for hours across an app's lifetime,
            // and DbContext is scoped, not built for that.
            using var scope = _scopeFactory.CreateScope();
            var services = scope.ServiceProvider;
            var db = services.GetRequiredService<AlgoForgeDbContext>();
            var pipeline = services.GetRequiredService<PersonPipelineService>();
            var env = services.GetRequiredService<IWebHostEnvironment>();

            var photo = await db.Photos.FirstOrDefaultAsync(p => p.Id == photoId, stoppingToken);
            if (photo is null || photo.FaceProcessingStatus == PhotoFaceProcessingStatus.Processed)
            {
                return;
            }

            var path = PhotoStorage.ResolveStoredPath(env, photo);
            if (path is null)
            {
                // The row survived but the file did not -- nothing to re-read. Marked Failed
                // rather than left Pending, so a status poll doesn't wait on it forever.
                photo.FaceProcessingStatus = PhotoFaceProcessingStatus.Failed;
                await db.SaveChangesAsync(stoppingToken);
                return;
            }

            await pipeline.ProcessPhotoAsync(photo, path);
            await db.SaveChangesAsync(stoppingToken);

            var stillPending = await db.Photos.CountAsync(
                p => p.EventId == photo.EventId && p.FaceProcessingStatus == PhotoFaceProcessingStatus.Pending,
                stoppingToken);

            if (stillPending == 0)
            {
                try
                {
                    await pipeline.ReclusterEventAsync(photo.EventId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex, "Auto re-cluster failed for event {EventId} after background detection.", photo.EventId);
                }
            }
        }
    }
}
