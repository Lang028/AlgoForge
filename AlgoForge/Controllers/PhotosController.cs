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
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<PhotosController> _logger;
        private readonly IEventAccessService _access;

        public PhotosController(
            AlgoForgeDbContext db,
            PersonPipelineService pipeline,
            UserManager<ApplicationUser> userManager,
            IWebHostEnvironment env,
            ILogger<PhotosController> logger,
            IEventAccessService access)
        {
            _db = db;
            _pipeline = pipeline;
            _userManager = userManager;
            _env = env;
            _logger = logger;
            _access = access;
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

            var (uploaded, skipped, detectionFailures) = await StoreAndDetectAsync(eventId, userId, files);

            // One clustering pass for the whole batch. A pipeline outage must not lose the
            // uploaded photos: they are already saved and can be re-clustered from the
            // review grid once the service is back.
            if (uploaded > 0 && !await TryReclusterAsync(eventId))
            {
                AddErrorMessage(
                    "Photos uploaded, but grouping people failed. Use Re-cluster on the identify page to retry.");
            }

            ReportOutcome(uploaded, skipped);
            ReportDetectionFailures(detectionFailures);
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

            var (uploaded, skipped, detectionFailures) = await StoreAndDetectAsync(eventId, userId, files);
            return Json(new { uploaded, skipped, detectionFailures });
        }

        // Runs the single clustering pass once the last batch has landed, and records the
        // run's totals for the gallery to show.
        //
        // The tallies come back from the client because they were accumulated across many
        // requests and no single one of them knows the whole story. They are display-only --
        // nothing is authorised or written from them -- so a client that reports nonsense
        // only misleads itself about its own upload.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequireEventRole(EventRole.Photographer)]
        public async Task<IActionResult> FinishUpload(
            Guid eventId, int uploaded, List<string>? skipped, int detectionFailures = 0)
        {
            var evt = await _db.Events.FindAsync(eventId);
            if (evt is null)
            {
                return NotFound();
            }

            var clustered = true;
            if (uploaded > 0)
            {
                clustered = await TryReclusterAsync(eventId);
            }

            ReportOutcome(uploaded, skipped ?? new List<string>());
            ReportDetectionFailures(detectionFailures);

            if (!clustered)
            {
                AddErrorMessage(
                    "Photos uploaded, but grouping people failed. Use Re-cluster on the identify page to retry.");
            }

            return Json(new { clustered });
        }

        // Stores each accepted file and runs detection on it. Shared by the plain form post
        // and the batched folder upload so both apply exactly the same validation.
        private async Task<(int Uploaded, List<string> Skipped, int DetectionFailures)> StoreAndDetectAsync(
            Guid eventId, Guid userId, List<IFormFile>? files)
        {
            // Model binding leaves this null when the form carries no file part at all,
            // which is exactly what a folder of nothing but sidecar files produces once the
            // browser-side filter has finished with it.
            files ??= new List<IFormFile>();

            // App_Data, not wwwroot: wwwroot is watched by dotnet watch / Visual Studio hot
            // reload, so writing uploads there makes every upload look like a source change --
            // the dev server re-evaluates the project and refreshes the browser mid-upload,
            // which killed the batched uploader from the photographer's point of view.
            // Program.cs maps this directory back onto the same /uploads URL.
            var uploadsDir = Path.Combine(
                _env.ContentRootPath, "App_Data", "uploads", eventId.ToString());
            Directory.CreateDirectory(uploadsDir);

            var uploaded = 0;
            var skipped = new List<string>();
            var detectionFailures = 0;

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
                var filePath = Path.Combine(uploadsDir, fileName);

                await using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream, HttpContext.RequestAborted);
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

                // Saved before detection runs, not after. Detection is the slow part -- a
                // network round trip to Python, seconds per photo -- and holding an unsaved
                // row across it meant a database hiccup during that gap lost a photo that
                // was already safely on disk. LocalDB stopping itself while idle is exactly
                // that hiccup, and it is why the connection is configured to retry at all.
                await _db.SaveChangesAsync();

                // Detection only -- no clustering here. Clustering is per-event and runs
                // once after the batch, which is both correct (photo 1 can now be grouped
                // using evidence from photo 300) and far cheaper than the old
                // re-cluster-per-photo approach.
                //
                // Still synchronous, and still the wrong place for this: D16 says never in
                // the request path. The seam to fix it is right here -- enqueue instead of
                // awaiting.
                var detectionSucceeded = await _pipeline.ProcessPhotoAsync(photo, filePath);
                if (!detectionSucceeded)
                {
                    detectionFailures++;
                }

                // Second save persists the detections and the processing status. If this
                // one fails the photograph itself is already stored and can be reprocessed;
                // it no longer takes the whole upload down with it.
                await _db.SaveChangesAsync();
                uploaded++;
            }

            return (uploaded, skipped, detectionFailures);
        }

        // POST /Photos/{eventId}/RetryDetection
        //
        // Runs detection again over the photos in an event that never got any. Until this
        // existed, a photo uploaded while the Python service was down was stuck: detection
        // only ran on the upload path, and Re-cluster cannot help because it groups
        // existing detections rather than creating them. The only remedy was deleting the
        // photographs and uploading them a second time.
        //
        // The originals are already on disk, so re-running detection is simply a matter of
        // reading them back. Photos that already succeeded are left alone.
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

            var recovered = 0;
            var stillFailing = 0;
            var missingFiles = 0;

            foreach (var photo in pending)
            {
                var path = ResolveStoredPath(photo);
                if (path is null)
                {
                    // The row survived but the file did not -- nothing to re-read.
                    missingFiles++;
                    continue;
                }

                if (await _pipeline.ProcessPhotoAsync(photo, path))
                {
                    recovered++;
                }
                else
                {
                    stillFailing++;
                }

                // Saved per photo rather than once at the end: this loop can run for
                // minutes over a few hundred photographs, and a failure halfway through
                // should keep the work already done.
                await _db.SaveChangesAsync();
            }

            if (recovered > 0 && !await TryReclusterAsync(eventId))
            {
                AddErrorMessage(
                    $"Detection recovered {recovered} photo(s), but grouping them failed. Try Re-cluster.");
                return RedirectToAction("Index", "Clusters", new { eventId });
            }

            var parts = new List<string>();
            if (recovered > 0) parts.Add($"{recovered} photo(s) now have people detected");
            if (stillFailing > 0) parts.Add($"{stillFailing} still failed -- is the person pipeline service running?");
            if (missingFiles > 0) parts.Add($"{missingFiles} had no file on disk");

            var summary = string.Join("; ", parts) + ".";

            if (recovered > 0)
            {
                TempData["SuccessMessage"] = summary;
            }
            else
            {
                AddErrorMessage(summary);
            }

            return RedirectToAction("Index", "Clusters", new { eventId });
        }

        private async Task<bool> TryReclusterAsync(Guid eventId)
        {
            try
            {
                await _pipeline.ReclusterEventAsync(eventId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Clustering failed for event {EventId} after upload.", eventId);
                return false;
            }
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

        private void ReportDetectionFailures(int detectionFailures)
        {
            if (detectionFailures <= 0)
            {
                return;
            }

            AddErrorMessage(
                $"{detectionFailures} photo(s) uploaded, but person detection failed. " +
                "Start the person pipeline service before uploading photos; these photos have no face detections yet.");
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
        [HttpGet]
        [Route("Photos/File/{id}")]
        public async Task<IActionResult> File(Guid id)
        {
            var photo = await _db.Photos.FirstOrDefaultAsync(p => p.Id == id);
            if (photo is null || photo.Status != PhotoStatus.Visible)
            {
                return NotFound();
            }

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

            var path = ResolveStoredPath(photo);
            if (path is null)
            {
                return NotFound();
            }

            // A private gallery shouldn't linger in a shared browser cache after access ends.
            Response.Headers.CacheControl = "no-store, no-cache, must-revalidate, private";

            return PhysicalFile(path, ContentTypeFor(path));
        }

        // GET /Photos/Download/{id} -- same access check as File, but sent as an
        // attachment. D5 and D20 give every member of the event, delegates included, the
        // right to keep a copy of the pictures they appear in.
        [HttpGet]
        [Route("Photos/Download/{id}")]
        public async Task<IActionResult> Download(Guid id)
        {
            var (photo, path) = await AuthorisedPhotoFileAsync(id);
            if (photo is null || path is null)
            {
                return photo is null ? NotFound() : NotFound();
            }

            Response.Headers.CacheControl = "no-store, no-cache, must-revalidate, private";

            var fileName = $"{photo.UploadedAt:yyyy-MM-dd}-{photo.Id.ToString()[..8]}{Path.GetExtension(path)}";
            return PhysicalFile(path, ContentTypeFor(path), fileName);
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
        // event, and returns the path on disk.
        private async Task<(Photo? Photo, string? Path)> AuthorisedPhotoFileAsync(Guid id)
        {
            var photo = await _db.Photos.FirstOrDefaultAsync(p => p.Id == id);
            if (photo is null || photo.Status != PhotoStatus.Visible)
            {
                return (null, null);
            }

            var userIdText = _userManager.GetUserId(User);
            if (userIdText is null || !Guid.TryParse(userIdText, out var viewerId))
            {
                return (null, null);
            }

            var allowed = await _access.HasAnyRoleAsync(viewerId, photo.EventId, new[]
            {
                EventRole.Coordinator, EventRole.Photographer, EventRole.Attendee, EventRole.Delegate
            }, HttpContext.RequestAborted);

            return allowed ? (photo, ResolveStoredPath(photo)) : (null, null);
        }

        // BlobUrl is "/uploads/{eventId}/{fileName}" -- map it back onto App_Data/uploads.
        private string? ResolveStoredPath(Photo photo)
        {
            var relative = photo.BlobUrl.StartsWith("/uploads/")
                ? photo.BlobUrl["/uploads/".Length..]
                : photo.BlobUrl.TrimStart('/');

            var root = Path.Combine(_env.ContentRootPath, "App_Data", "uploads");
            var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));

            // Never let a stored value escape the uploads root.
            if (!full.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return System.IO.File.Exists(full) ? full : null;
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
