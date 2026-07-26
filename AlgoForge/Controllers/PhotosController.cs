using AlgoForge.Data;
using AlgoForge.Models;
using AlgoForge.Services.PersonPipeline;
using AlgoForge.ViewModels.Photos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AlgoForge.Controllers
{
    [Authorize]
    public class PhotosController : Controller
    {
        private readonly AlgoForgeDbContext _db;
        private readonly PersonPipelineService _pipeline;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<PhotosController> _logger;

        public PhotosController(
            AlgoForgeDbContext db,
            PersonPipelineService pipeline,
            UserManager<ApplicationUser> userManager,
            IWebHostEnvironment env,
            ILogger<PhotosController> logger)
        {
            _db = db;
            _pipeline = pipeline;
            _userManager = userManager;
            _env = env;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Upload(Guid eventId)
        {
            var evt = await _db.Events.FindAsync(eventId);
            if (evt is null)
            {
                return NotFound();
            }

            ViewData["EventId"] = eventId;
            ViewData["EventName"] = evt.Name;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(50_000_000)]
        public async Task<IActionResult> Upload(Guid eventId, List<IFormFile> files)
        {
            var evt = await _db.Events.FindAsync(eventId);
            if (evt is null)
            {
                return NotFound();
            }

            var userIdText = _userManager.GetUserId(User);
            if (userIdText is null || !Guid.TryParse(userIdText, out var userId))
            {
                return Challenge();
            }

            var uploadsDir = Path.Combine(_env.WebRootPath, "uploads", eventId.ToString());
            Directory.CreateDirectory(uploadsDir);

            foreach (var file in files)
            {
                if (file.Length == 0)
                {
                    continue;
                }

                var photoId = Guid.NewGuid();
                var extension = Path.GetExtension(file.FileName);
                var fileName = $"{photoId}{extension}";
                var filePath = Path.Combine(uploadsDir, fileName);

                await using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                var photo = new Photo
                {
                    Id = photoId,
                    EventId = eventId,
                    UploadedByUserId = userId,
                    BlobUrl = $"/uploads/{eventId}/{fileName}",
                    UploadedAt = DateTime.UtcNow
                };

                _db.Photos.Add(photo);

                // Detection only -- no clustering here. Clustering is per-event and runs
                // once after the batch, which is both correct (photo 1 can now be grouped
                // using evidence from photo 300) and far cheaper than the old
                // re-cluster-per-photo approach.
                //
                // Still synchronous, and still the wrong place for this: D16 says never in
                // the request path, and at ~1-3s per photo a large batch will time out.
                // The seam to fix it is right here -- enqueue instead of awaiting.
                await _pipeline.ProcessPhotoAsync(photo, filePath);
                await _db.SaveChangesAsync();
            }

            // One clustering pass for the whole batch. A pipeline outage must not lose the
            // uploaded photos: they are already saved and can be re-clustered from the
            // review grid once the service is back.
            try
            {
                await _pipeline.ReclusterEventAsync(eventId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Clustering failed for event {EventId} after upload.", eventId);
                TempData["ErrorMessage"] =
                    "Photos uploaded, but grouping people failed. Use Re-cluster on the identify page to retry.";
            }

            return RedirectToAction(nameof(Index), new { eventId });
        }

        // The gallery shows every visible photo to every event member -- the photographs are
        // the event's, and they stay up regardless of who has connected with whom.
        //
        // What consent gates is *identity*, not the photograph: a name only ever appears on a
        // face whose Tag is Confirmed. Suggested tags (identified by the coordinator but not
        // yet agreed to) and unidentified faces render no box at all. Deliberately: reading
        // the cluster link here instead would name people before they'd agreed, and would keep
        // naming them after they'd rejected the tag.
        public async Task<IActionResult> Index(Guid eventId)
        {
            var evt = await _db.Events.FindAsync(eventId);
            if (evt is null)
            {
                return NotFound();
            }

            var userIdText = _userManager.GetUserId(User);
            if (userIdText is null || !Guid.TryParse(userIdText, out var viewerId))
            {
                return Challenge();
            }

            // The IsTaggable filter is mandatory on every attendee-facing query
            // (PERSON_MATCHING_PLAN.md 4.3). Tier B detections -- backshots, blurry
            // background figures, passers-by -- are internal clustering evidence and must
            // never reach a view, not even as an anonymous box or a count.
            var photos = await _db.Photos
                .Where(p => p.EventId == eventId && p.Status == PhotoStatus.Visible)
                .Include(p => p.PersonDetections.Where(d => d.IsTaggable))
                .OrderByDescending(p => p.UploadedAt)
                .ToListAsync();

            var detectionIds = photos.SelectMany(p => p.PersonDetections).Select(d => d.Id).ToList();

            var confirmedTags = await _db.Tags
                .Where(t => t.Status == TagStatus.Confirmed && detectionIds.Contains(t.PersonDetectionId))
                .Include(t => t.TaggedAttendee)
                .ToListAsync();

            // One face can only carry one identity, so first-per-detection is enough.
            var tagByDetection = confirmedTags
                .GroupBy(t => t.PersonDetectionId)
                .ToDictionary(g => g.Key, g => g.First());

            var connections = await _db.Connections
                .Where(c => c.RequesterId == viewerId || c.ReceiverId == viewerId)
                .ToListAsync();

            var connectionByOtherUser = connections.ToDictionary(c => c.OtherUserId(viewerId));

            var model = new GalleryViewModel
            {
                EventId = eventId,
                EventName = evt.Name,
                ConfirmedPeopleCount = confirmedTags.Select(t => t.TaggedAttendeeId).Distinct().Count(),
                AwaitingConsentCount = detectionIds.Count - tagByDetection.Count
            };

            foreach (var photo in photos)
            {
                var galleryPhoto = new GalleryPhoto
                {
                    Id = photo.Id,
                    Url = photo.BlobUrl,
                    UploadedAt = photo.UploadedAt
                };

                foreach (var detection in photo.PersonDetections)
                {
                    if (!tagByDetection.TryGetValue(detection.Id, out var tag) || tag.TaggedAttendee is null)
                    {
                        continue;
                    }

                    galleryPhoto.Faces.Add(BuildFace(detection, tag.TaggedAttendee, viewerId, connectionByOtherUser));
                }

                model.Photos.Add(galleryPhoto);
            }

            return View(model);
        }

        private static GalleryFace BuildFace(
            PersonDetection detection,
            Attendee attendee,
            Guid viewerId,
            IReadOnlyDictionary<Guid, Connection> connectionByOtherUser)
        {
            // Draw the face box, falling back to the person box only if a Tier A
            // detection somehow lacks one. A name label belongs on a face, not on a
            // full-body rectangle that would also cover whoever is standing behind them.
            var face = new GalleryFace
            {
                DetectionId = detection.Id,
                AttendeeId = attendee.Id,
                Name = attendee.Name,
                BoxX = detection.FaceX ?? detection.BoxX,
                BoxY = detection.FaceY ?? detection.BoxY,
                BoxWidth = detection.FaceWidth ?? detection.BoxWidth,
                BoxHeight = detection.FaceHeight ?? detection.BoxHeight
            };

            var targetUserId = attendee.ClaimedByUserId;

            if (targetUserId is null)
            {
                // Only reachable when the event has TagConfirmationRequired off, which
                // auto-confirms tags on people who haven't claimed their invite yet.
                face.State = FaceConnectionState.NoAccount;
                return face;
            }

            if (targetUserId == viewerId)
            {
                face.State = FaceConnectionState.Self;
                face.ContactEmail = attendee.Email;
                face.ContactInfo = attendee.ContactInfo;
                return face;
            }

            connectionByOtherUser.TryGetValue(targetUserId.Value, out var connection);

            face.ConnectionId = connection?.Id;
            face.State = connection?.Status switch
            {
                ConnectionStatus.Accepted => FaceConnectionState.Connected,
                ConnectionStatus.Declined => FaceConnectionState.Declined,
                ConnectionStatus.Pending when connection.ReceiverId == viewerId => FaceConnectionState.AwaitingYou,
                ConnectionStatus.Pending => FaceConnectionState.Sent,
                _ => FaceConnectionState.Open
            };

            // D11: an accepted connection shares contacts; so does the attendee's own opt-in.
            // Anything else and the fields simply never leave the server.
            if (face.State == FaceConnectionState.Connected || attendee.ContactsVisible)
            {
                face.ContactEmail = attendee.Email;
                face.ContactInfo = attendee.ContactInfo;
                face.ContactsSharedByOptIn = face.State != FaceConnectionState.Connected;
            }

            return face;
        }
    }
}
