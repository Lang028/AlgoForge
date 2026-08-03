using AlgoForge.Data;
using AlgoForge.Models;
using AlgoForge.Services;
using AlgoForge.Services.Authorization;
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
        private readonly PhotoDetectionQueue _detectionQueue;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<PhotosController> _logger;
        private readonly IEventAccessService _access;
        private readonly IPhotoStorage _storage;

        public PhotosController(
            AlgoForgeDbContext db,
            PersonPipelineService pipeline,
            PhotoDetectionQueue detectionQueue,
            UserManager<ApplicationUser> userManager,
            IWebHostEnvironment env,
            ILogger<PhotosController> logger,
            IEventAccessService access,
            IPhotoStorage storage)
        {
            _db = db;
            _pipeline = pipeline;
            _detectionQueue = detectionQueue;
            _userManager = userManager;
            _env = env;
            _logger = logger;
            _access = access;
            _storage = storage;
        }

        [HttpGet]
        [RequireEventRole(EventRole.Photographer)]
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
        [RequireEventRole(EventRole.Photographer)]
        [RequestSizeLimit(PhotoUploadPolicy.MaxRequestBytes)]
        [RequestFormLimits(MultipartBodyLengthLimit = PhotoUploadPolicy.MaxRequestBytes)]
        public async Task<IActionResult> Upload(Guid eventId, List<IFormFile>? files)
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

            var (uploaded, skipped) = await StoreAndDetectAsync(eventId, userId, files);

            ReportOutcome(uploaded, skipped);
            return RedirectToAction(nameof(Index), new { eventId });
        }

        // --- Folder upload (the batched path) -------------------------------------
        //
        // A folder off an event shoot is hundreds of files and several gigabytes, which the
        // single-request form above cannot carry however high the size cap goes: detection
        // runs inline at ~1-3s per photo (D16's known shortcut), so 300 photos is upwards of
        // ten minutes in one request, past every proxy and browser timeout there is.
        //
        // So the browser slices the folder into small batches and posts them one after
        // another, each its own short request. That also buys a real progress indicator and
        // makes a mid-way failure cost one batch instead of the entire folder.
        //
        // Clustering is deliberately NOT run per batch. It is O(n^2) over the event's
        // detections and it is the whole event's grouping, so running it 40 times while a
        // folder uploads would be slow and pointless -- the client calls FinishUpload once
        // at the end instead.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireEventRole(EventRole.Photographer)]
        [RequestSizeLimit(PhotoUploadPolicy.MaxRequestBytes)]
        [RequestFormLimits(MultipartBodyLengthLimit = PhotoUploadPolicy.MaxRequestBytes)]
        public async Task<IActionResult> UploadBatch(Guid eventId, List<IFormFile>? files)
        {
            var evt = await _db.Events.FindAsync(eventId);
            if (evt is null)
            {
                return NotFound();
            }

            var userIdText = _userManager.GetUserId(User);
            if (userIdText is null || !Guid.TryParse(userIdText, out var userId))
            {
                return Unauthorized();
            }

            var (uploaded, skipped) = await StoreAndDetectAsync(eventId, userId, files);
            return Json(new { uploaded, skipped });
        }

        // Records the run's totals for the gallery to show, once the last batch has landed.
        //
        // The tallies come back from the client because they were accumulated across many
        // requests and no single one of them knows the whole story. They are display-only --
        // nothing is authorised or written from them -- so a client that reports nonsense
        // only misleads itself about its own upload.
        //
        // Clustering used to run here too, once per whole upload. It no longer does: detection
        // itself is now a background queue (PhotoDetectionWorker), so the photos this call
        // reports on may not be detected yet, and clustering them now would just cluster
        // whatever happened to finish first. The worker triggers clustering itself once the
        // event's queue is actually empty.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireEventRole(EventRole.Photographer)]
        public async Task<IActionResult> FinishUpload(Guid eventId, int uploaded, List<string>? skipped)
        {
            var evt = await _db.Events.FindAsync(eventId);
            if (evt is null)
            {
                return NotFound();
            }

            ReportOutcome(uploaded, skipped ?? new List<string>());

            return Json(new { uploaded });
        }

        // Stores each accepted file and queues it for detection. Shared by the plain form
        // post and the batched folder upload so both apply exactly the same validation.
        private async Task<(int Uploaded, List<string> Skipped)> StoreAndDetectAsync(
            Guid eventId, Guid userId, List<IFormFile>? files)
        {
            // Model binding leaves this null when the form carries no file part at all,
            // which is exactly what a folder of nothing but sidecar files produces once the
            // browser-side filter has finished with it.
            files ??= new List<IFormFile>();

            // Where the bytes land is IPhotoStorage's problem: App_Data in development,
            // Azure Blob in production. See IPhotoStorage for why the two differ.
            //
            // A link-shared gallery runs no detection at all, so nothing here is queued and
            // no face embedding is ever computed for these photographs. Enforced at the
            // point of upload rather than by hiding the results later: there is nobody to
            // ask for consent on such an event, so there must be nothing collected that
            // consent would have been needed for.
            var detects = await _db.Events
                .Where(e => e.Id == eventId)
                .Select(e => e.GalleryMode)
                .FirstOrDefaultAsync(HttpContext.RequestAborted) == EventGalleryMode.Consent;

            var uploaded = 0;
            var skipped = new List<string>();

            foreach (var file in files)
            {
                // Sniffed server-side, and the extension it returns is the one the file is
                // stored under -- see PhotoUploadPolicy for why the client's filename is
                // never trusted for this.
                var inspection = await PhotoUploadPolicy.InspectAsync(file, HttpContext.RequestAborted);
                if (!inspection.Accepted)
                {
                    // A folder upload legitimately contains non-images. Skipping them with a
                    // reason is the correct outcome, not an error -- failing the batch
                    // because of one Thumbs.db would make folder upload unusable.
                    skipped.Add($"{SafeDisplayName(file.FileName)} ({inspection.Reason})");
                    continue;
                }

                var photoId = Guid.NewGuid();
                var fileName = $"{photoId}{inspection.Extension}";

                string blobUrl;
                await using (var source = file.OpenReadStream())
                {
                    blobUrl = await _storage.SaveAsync(
                        eventId, fileName, source, HttpContext.RequestAborted);
                }

                var photo = new Photo
                {
                    Id = photoId,
                    EventId = eventId,
                    UploadedByUserId = userId,
                    BlobUrl = blobUrl,
                    UploadedAt = DateTime.UtcNow,
                    FaceProcessingStatus = detects
                        ? PhotoFaceProcessingStatus.Pending
                        : PhotoFaceProcessingStatus.NotApplicable
                };

                _db.Photos.Add(photo);

                // Saved before detection runs, not after. A database hiccup during detection
                // must not lose a photo that is already safely on disk. LocalDB stopping
                // itself while idle is exactly that hiccup, and it is why the connection is
                // configured to retry at all.
                await _db.SaveChangesAsync();

                // Detection runs off this request entirely (PhotoDetectionWorker) -- the row
                // is saved with FaceProcessingStatus.Pending and the queue is what moves it
                // to Processed or Failed. Clustering follows automatically once the whole
                // event's queue drains, not from here.
                if (detects)
                {
                    _detectionQueue.Enqueue(photo.Id);
                }

                uploaded++;
            }

            return (uploaded, skipped);
        }

        // POST /Photos/{eventId}/RetryDetection
        //
        // Queues detection again for the photos in an event that never got any. Until this
        // existed, a photo uploaded while the Python service was down was stuck: detection
        // only ran on the upload path, and Re-cluster cannot help because it groups
        // existing detections rather than creating them. The only remedy was deleting the
        // photographs and uploading them a second time.
        //
        // This is also the no-JavaScript fallback for the identify page's progress bar (see
        // RetryDetectionPending/Batch below). It used to be the ONLY path, and used to block
        // the request until every photo was done -- minutes, for a few hundred photos, with
        // nothing on screen. Now it just enqueues and redirects immediately; the background
        // worker does the rest regardless of which path queued the work.
        [HttpPost]
        [Route("Photos/{eventId}/RetryDetection")]
        [ValidateAntiForgeryToken]
        [RequireEventRole(EventRole.Coordinator, EventRole.Photographer)]
        public async Task<IActionResult> RetryDetection(Guid eventId)
        {
            var pending = await _db.Photos
                .Where(p => p.EventId == eventId
                            && p.Status == PhotoStatus.Visible
                            && p.FaceProcessingStatus != PhotoFaceProcessingStatus.Processed)
                .ToListAsync();

            if (pending.Count == 0)
            {
                TempData["SuccessMessage"] = "Every photo in this event has already been processed.";
                return RedirectToAction("Index", "Clusters", new { eventId });
            }

            // See RetryDetectionBatch for why this flips to Pending rather than just enqueueing:
            // a retried photo starts at Failed, and DetectionStatus needs Pending to mean
            // "queued or in flight" for a retry the same way it already does for an upload.
            foreach (var photo in pending)
            {
                photo.FaceProcessingStatus = PhotoFaceProcessingStatus.Pending;
            }
            await _db.SaveChangesAsync();

            foreach (var photo in pending)
            {
                _detectionQueue.Enqueue(photo.Id);
            }

            TempData["SuccessMessage"] =
                $"Detection queued for {pending.Count} photo(s) -- refresh this page in a bit to see the new groups.";
            return RedirectToAction("Index", "Clusters", new { eventId });
        }

        // --- Retry detection, polled for a progress bar ----------------------------
        //
        // The plain RetryDetection action above stays as the no-JavaScript fallback. With
        // JavaScript the identify page instead asks for the pending photo ids, queues them,
        // then polls DetectionStatus until the event's queue is empty -- there is nothing
        // left here to block on or batch for timeout reasons, since queueing is instant;
        // the batching that remains is just to keep any one request's photoIds list modest.
        [HttpGet]
        [RequireEventRole(EventRole.Coordinator, EventRole.Photographer)]
        public async Task<IActionResult> RetryDetectionPending(Guid eventId)
        {
            var photoIds = await _db.Photos
                .Where(p => p.EventId == eventId
                            && p.Status == PhotoStatus.Visible
                            && p.FaceProcessingStatus != PhotoFaceProcessingStatus.Processed)
                .Select(p => p.Id)
                .ToListAsync();

            return Json(new { photoIds });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireEventRole(EventRole.Coordinator, EventRole.Photographer)]
        public async Task<IActionResult> RetryDetectionBatch(Guid eventId, List<Guid> photoIds)
        {
            photoIds ??= new List<Guid>();

            // Re-validated against the event rather than trusted: the id list is client
            // supplied, and this is the only thing standing between a crafted request and
            // queueing detection for a photo in a different event.
            var photos = await _db.Photos
                .Where(p => p.EventId == eventId
                            && photoIds.Contains(p.Id)
                            && p.Status == PhotoStatus.Visible
                            && p.FaceProcessingStatus != PhotoFaceProcessingStatus.Processed)
                .ToListAsync();

            // A retried photo is sitting at Failed, not Pending -- enqueueing alone doesn't
            // change that row, so DetectionStatus (and the progress bar polling it) would see
            // nothing "pending" until the worker actually gets to it. Marking it Pending here,
            // the moment it's queued, is what makes "pending" mean "queued or in flight" for a
            // retry the same way it already does for a fresh upload.
            foreach (var photo in photos)
            {
                photo.FaceProcessingStatus = PhotoFaceProcessingStatus.Pending;
            }
            await _db.SaveChangesAsync();

            foreach (var photo in photos)
            {
                _detectionQueue.Enqueue(photo.Id);
            }

            return Json(new { enqueued = photos.Count });
        }

        // Polled by both the upload page and the identify page's retry-detection progress
        // bar: "how much of this event's queue is left" is the same question either way,
        // and the answer lives entirely on the Photos rows -- no separate job-tracking state
        // to keep in sync with them.
        [HttpGet]
        [RequireEventRole(EventRole.Coordinator, EventRole.Photographer)]
        public async Task<IActionResult> DetectionStatus(Guid eventId)
        {
            var counts = await _db.Photos
                .Where(p => p.EventId == eventId && p.Status == PhotoStatus.Visible)
                .GroupBy(p => p.FaceProcessingStatus)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();

            int CountOf(PhotoFaceProcessingStatus status) =>
                counts.FirstOrDefault(c => c.Status == status)?.Count ?? 0;

            return Json(new
            {
                pending = CountOf(PhotoFaceProcessingStatus.Pending),
                processed = CountOf(PhotoFaceProcessingStatus.Processed),
                failed = CountOf(PhotoFaceProcessingStatus.Failed)
            });
        }

        // Says what actually happened to the selection. Silence here is what made the old
        // behaviour confusing: a file the server quietly declined simply never appeared in
        // the gallery, with nothing anywhere to say why.
        private void ReportOutcome(int uploaded, List<string> skipped)
        {
            if (uploaded > 0)
            {
                var message = uploaded == 1 ? "1 photo uploaded." : $"{uploaded} photos uploaded.";
                if (skipped.Count > 0)
                {
                    message += $" {skipped.Count} file(s) skipped.";
                }

                // Detection and clustering now run in the background (PhotoDetectionWorker)
                // rather than being held open on this request -- see that class for why.
                message += " Detecting faces in the background -- refresh in a bit to see the new groups.";

                TempData["SuccessMessage"] = message;
            }

            if (skipped.Count == 0)
            {
                if (uploaded == 0)
                {
                    AddErrorMessage("No files were selected.");
                }

                return;
            }

            // Naming every skipped file in a folder of hundreds would bury the message it is
            // attached to, so the list is capped and the remainder counted.
            const int maxNamed = 10;
            var named = string.Join(", ", skipped.Take(maxNamed));
            if (skipped.Count > maxNamed)
            {
                named += $" and {skipped.Count - maxNamed} more";
            }

            var detail = $"Skipped: {named}.";
            AddErrorMessage(uploaded > 0
                ? detail
                : $"Nothing was uploaded. {detail}");
        }

        private void AddErrorMessage(string message)
        {
            TempData["ErrorMessage"] = TempData["ErrorMessage"] is string existing && !string.IsNullOrWhiteSpace(existing)
                ? $"{existing} {message}"
                : message;
        }

        // The uploaded filename is attacker-controlled and is about to be echoed into a
        // page. Razor encodes it, but a folder upload can carry a relative path
        // ("subfolder/IMG_1.jpg") and a 200-character name, so it is trimmed to the leaf
        // and bounded before it ever reaches the view.
        private static string SafeDisplayName(string? fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return "(unnamed)";
            }

            var leaf = fileName.Replace('\\', '/').Split('/').Last();
            return leaf.Length <= 60 ? leaf : string.Concat(leaf.AsSpan(0, 57), "...");
        }

        // Serves the image bytes for one photo, gated on membership of that photo's event.
        //
        // Photos used to be served straight off disk by a static-file provider mapped to
        // /uploads, which meant anyone holding (or guessing) a URL could read a private
        // event's pictures with no account at all -- and the URL survived being removed
        // from the event. Every image in the app now comes through here instead.
        // Anonymous is allowed *in* only so the share cookie can be examined -- the checks
        // inside still refuse everyone who does not hold either it or event membership.
        // Without this the controller's [Authorize] would redirect a link-shared viewer to
        // a sign-in page for an account they were never meant to need.
        [AllowAnonymous]
        [HttpGet]
        [Route("Photos/File/{id}")]
        public async Task<IActionResult> File(Guid id)
        {
            var photo = await _db.Photos.FirstOrDefaultAsync(p => p.Id == id);
            if (photo is null || photo.Status != PhotoStatus.Visible)
            {
                return NotFound();
            }

            // Tried first, and only ever satisfied by a link-shared event: a viewer here has
            // no account at all, so the membership check below would send them to a sign-in
            // page for a gallery whose whole premise is that it does not need one.
            if (!await HoldsShareCookieAsync(photo.EventId))
            {
                var userIdText = _userManager.GetUserId(User);
                if (userIdText is null || !Guid.TryParse(userIdText, out var viewerId))
                {
                    return Challenge();
                }

                var allowed = await _access.HasAnyRoleAsync(viewerId, photo.EventId, new[]
                {
                    EventRole.Coordinator, EventRole.Photographer, EventRole.Attendee, EventRole.Delegate
                }, HttpContext.RequestAborted);

                if (!allowed)
                {
                    return Forbid();
                }
            }

            var stream = await _storage.OpenReadAsync(photo, HttpContext.RequestAborted);
            if (stream is null)
            {
                return NotFound();
            }

            // A private gallery shouldn't linger in a shared browser cache after access ends.
            Response.Headers.CacheControl = "no-store, no-cache, must-revalidate, private";

            // base.File, not this action: the bytes are streamed from wherever storage put
            // them, which in production is a blob and has no path to hand to PhysicalFile.
            return base.File(stream, ContentTypeFor(photo.BlobUrl));
        }

        // GET /Photos/Download/{id} -- same access check as File, but sent as an
        // attachment. D5 and D20 give every member of the event, delegates included, the
        // right to keep a copy of the pictures they appear in.
        // See File: anonymous in, still refused without the cookie or membership.
        [AllowAnonymous]
        [HttpGet]
        [Route("Photos/Download/{id}")]
        public async Task<IActionResult> Download(Guid id)
        {
            var (photo, stream) = await AuthorisedPhotoFileAsync(id);
            if (photo is null || stream is null)
            {
                return NotFound();
            }

            Response.Headers.CacheControl = "no-store, no-cache, must-revalidate, private";

            var extension = Path.GetExtension(photo.BlobUrl);
            var fileName = $"{photo.UploadedAt:yyyy-MM-dd}-{photo.Id.ToString()[..8]}{extension}";
            return base.File(stream, ContentTypeFor(photo.BlobUrl), fileName);
        }

        // POST /Photos/{id}/Delete -- a soft hide, not an erase (D8). The file stays on
        // disk and the row keeps its detections and tags, so a takedown is reversible and
        // the audit trail survives. Staff only: the people who ran the event decide what
        // comes down.
        [HttpPost]
        [Route("Photos/{id}/Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(Guid id)
        {
            var photo = await _db.Photos.FirstOrDefaultAsync(p => p.Id == id);
            if (photo is null)
            {
                return NotFound();
            }

            var userIdText = _userManager.GetUserId(User);
            if (userIdText is null || !Guid.TryParse(userIdText, out var viewerId))
            {
                return Challenge();
            }

            var allowed = await _access.HasAnyRoleAsync(viewerId, photo.EventId,
                new[] { EventRole.Coordinator, EventRole.Photographer }, HttpContext.RequestAborted);

            if (!allowed)
            {
                return Forbid();
            }

            photo.Status = PhotoStatus.Hidden;
            await _db.SaveChangesAsync();

            TempData["SuccessMessage"] = "Photo removed from the gallery.";
            return RedirectToAction(nameof(Index), new { eventId = photo.EventId });
        }

        // Shared by File and Download: resolves the photo, checks the viewer belongs to its
        // event, and opens its bytes.
        private async Task<(Photo? Photo, Stream? Content)> AuthorisedPhotoFileAsync(Guid id)
        {
            var photo = await _db.Photos.FirstOrDefaultAsync(p => p.Id == id);
            if (photo is null || photo.Status != PhotoStatus.Visible)
            {
                return (null, null);
            }

            // Downloading is the point of a shared gallery, not a concession -- somebody
            // handed a link is there to keep the photographs.
            if (!await HoldsShareCookieAsync(photo.EventId))
            {
                var userIdText = _userManager.GetUserId(User);
                if (userIdText is null || !Guid.TryParse(userIdText, out var viewerId))
                {
                    return (null, null);
                }

                var allowed = await _access.HasAnyRoleAsync(viewerId, photo.EventId, new[]
                {
                    EventRole.Coordinator, EventRole.Photographer, EventRole.Attendee, EventRole.Delegate
                }, HttpContext.RequestAborted);

                if (!allowed)
                {
                    return (null, null);
                }
            }

            return (photo, await _storage.OpenReadAsync(photo, HttpContext.RequestAborted));
        }

        // True only when the request carries a cookie holding this event's current share
        // token, and the event is actually a link-shared one.
        //
        // The token is re-read from the database on every request rather than trusted from
        // the cookie alone, so rotating it shuts out browsers that were already admitted --
        // otherwise "revoke the link" would mean nothing to whoever had already used it.
        // Consent events are refused outright: no cookie may ever stand in for membership
        // of a gallery whose photographs attendees agreed to share with members only.
        private async Task<bool> HoldsShareCookieAsync(Guid eventId)
        {
            var presented = Request.Cookies[ShareController.CookieName(eventId)];
            if (string.IsNullOrEmpty(presented))
            {
                return false;
            }

            var expected = await _db.Events
                .Where(e => e.Id == eventId && e.GalleryMode == EventGalleryMode.LinkShared)
                .Select(e => e.ShareToken)
                .FirstOrDefaultAsync(HttpContext.RequestAborted);

            return !string.IsNullOrEmpty(expected)
                && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.UTF8.GetBytes(presented),
                    System.Text.Encoding.UTF8.GetBytes(expected));
        }

        private static string ContentTypeFor(string path) =>
            Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                ".bmp" => "image/bmp",
                _ => "image/jpeg"
            };

        // The gallery shows every visible photo to every event member -- the photographs are
        // the event's, and they stay up regardless of who has connected with whom.
        //
        // What consent gates is *identity*, not the photograph: a name only ever appears on a
        // face whose Tag is Confirmed. Suggested tags (identified by the coordinator but not
        // yet agreed to) and unidentified faces render no box at all. Deliberately: reading
        // the cluster link here instead would name people before they'd agreed, and would keep
        // naming them after they'd rejected the tag.
        //
        // Open to every role in the event, including Delegate: D20 makes the gallery a
        // normal complete gallery for members, and D5 gives delegates view + download.
        [RequireEventRole(
            EventRole.Coordinator, EventRole.Photographer, EventRole.Attendee, EventRole.Delegate)]
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

            // One resolve of the viewer's roles drives the whole toolbar, so the page can't
            // offer an action the request would then refuse.
            var viewerRoles = await _access.ResolveRolesAsync(viewerId, eventId, HttpContext.RequestAborted);

            var model = new GalleryViewModel
            {
                EventId = eventId,
                EventName = evt.Name,
                ConfirmedPeopleCount = confirmedTags.Select(t => t.TaggedAttendeeId).Distinct().Count(),
                AwaitingConsentCount = detectionIds.Count - tagByDetection.Count,
                CanUpload = viewerRoles.Contains(EventRole.Photographer),
                CanIdentify = viewerRoles.Contains(EventRole.Coordinator) || viewerRoles.Contains(EventRole.Photographer),
                CanDelete = viewerRoles.Contains(EventRole.Coordinator) || viewerRoles.Contains(EventRole.Photographer),
                IsAttendee = viewerRoles.Contains(EventRole.Attendee),
                IsDelegate = viewerRoles.Contains(EventRole.Delegate)
            };

            foreach (var photo in photos)
            {
                var galleryPhoto = new GalleryPhoto
                {
                    Id = photo.Id,
                    Url = Url.Action(nameof(File), "Photos", new { id = photo.Id })!,
                    DownloadUrl = Url.Action(nameof(Download), "Photos", new { id = photo.Id })!,
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
                // Shown to every member. It is what the person chose to say about
                // themselves, not a means of contacting them, so it isn't gated on a
                // connection the way ContactEmail and ContactInfo are below.
                About = string.IsNullOrWhiteSpace(attendee.About) ? null : attendee.About,
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
