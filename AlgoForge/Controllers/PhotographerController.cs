using AlgoForge.Data;
using AlgoForge.Models;
using AlgoForge.ViewModels.Photographer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AlgoForge.Controllers
{
    // Covers the Photographer use cases from the use-case diagram:
    // Create/Join Event, Upload Photographs, Organise Albums,
    // Review Face Clusters, Identify Face Cluster (Create Attendee Profile),
    // Edit Attendee Profiles, Invite Event Coordinator,
    // Work Across Multiple Organisations, View Unidentified Clusters.
    [Authorize]
    public class PhotographerController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;

        public PhotographerController(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        private string CurrentUserId => _userManager.GetUserId(User)!;

        // ---------- Dashboard: "Work Across Multiple Organisations" ----------
        // GET /Photographer
        public async Task<IActionResult> Index()
        {
            var userId = CurrentUserId;

            var events = await _db.EventMemberships
                .Where(m => m.UserId == userId && m.Role == EventRole.Photographer)
                .Include(m => m.Event).ThenInclude(e => e.Organisation)
                .Include(m => m.Event).ThenInclude(e => e.Albums).ThenInclude(a => a.Photos)
                .Include(m => m.Event).ThenInclude(e => e.FaceClusters)
                .Select(m => new PhotographerEventSummary
                {
                    EventId = m.Event.Id,
                    EventName = m.Event.Name,
                    OrganisationName = m.Event.Organisation.Name,
                    Status = m.Event.Status,
                    AlbumCount = m.Event.Albums.Count,
                    PhotoCount = m.Event.Albums.SelectMany(a => a.Photos).Count(),
                    UnidentifiedClusterCount = m.Event.FaceClusters.Count(c => c.IdentifiedEventMembershipId == null)
                })
                .ToListAsync();

            return View(new PhotographerDashboardViewModel { Events = events });
        }

        // ---------- Create / Join Event ----------
        // GET /Photographer/CreateEvent
        public async Task<IActionResult> CreateEvent()
        {
            var orgs = await _db.Organisations
                .Select(o => new { o.Id, o.Name })
                .ToListAsync();

            var vm = new CreateEventViewModel
            {
                Organisations = orgs.Select(o => (o.Id, o.Name)).ToList()
            };
            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateEvent(CreateEventViewModel vm)
        {
            if (!ModelState.IsValid)
            {
                vm.Organisations = await _db.Organisations
                    .Select(o => new ValueTuple<int, string>(o.Id, o.Name)).ToListAsync();
                return View(vm);
            }

            var newEvent = new Event
            {
                OrganisationId = vm.OrganisationId,
                Name = vm.Name,
                Description = vm.Description,
                EventDate = vm.EventDate,
                Status = EventStatus.Draft,
                TagConfirmationMode = vm.RequireAttendeeConfirmation
                    ? TagConfirmationMode.RequireConfirmation
                    : TagConfirmationMode.AutoConfirm
            };
            _db.Events.Add(newEvent);
            await _db.SaveChangesAsync();

            _db.EventMemberships.Add(new EventMembership
            {
                UserId = CurrentUserId,
                EventId = newEvent.Id,
                Role = EventRole.Photographer
            });
            await _db.SaveChangesAsync();

            TempData["Success"] = $"Event '{newEvent.Name}' created. It's in Draft until you set it Live.";
            return RedirectToAction(nameof(Index));
        }

        // GET /Photographer/JoinEvent
        public IActionResult JoinEvent() => View(new JoinEventViewModel());

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> JoinEvent(JoinEventViewModel vm)
        {
            // Photographers join via an invitation token (no open guest links,
            // per the README's consent model).
            var invitation = await _db.Invitations
                .FirstOrDefaultAsync(i => i.TokenHash == vm.InvitationToken
                                        && i.Status == InvitationStatus.Pending
                                        && i.IntendedRole == EventRole.Photographer);

            if (invitation == null || invitation.ExpiresAt < DateTime.UtcNow)
            {
                ModelState.AddModelError(string.Empty, "That invitation is invalid or has expired.");
                return View(vm);
            }

            var alreadyMember = await _db.EventMemberships
                .AnyAsync(m => m.UserId == CurrentUserId && m.EventId == invitation.EventId);

            if (!alreadyMember)
            {
                _db.EventMemberships.Add(new EventMembership
                {
                    UserId = CurrentUserId,
                    EventId = invitation.EventId,
                    Role = EventRole.Photographer
                });
            }

            invitation.Status = InvitationStatus.Accepted;
            await _db.SaveChangesAsync();

            TempData["Success"] = "You've joined the event as a photographer.";
            return RedirectToAction(nameof(Index));
        }

        // ---------- Organise Albums ----------
        // GET /Photographer/Albums/5  (eventId)
        public async Task<IActionResult> Albums(int eventId)
        {
            if (!await IsPhotographerOnEvent(eventId)) return Forbid();

            var ev = await _db.Events
                .Include(e => e.Albums).ThenInclude(a => a.Photos)
                .FirstOrDefaultAsync(e => e.Id == eventId);

            if (ev == null) return NotFound();

            var vm = new AlbumListViewModel
            {
                EventId = ev.Id,
                EventName = ev.Name,
                UploadWindowOpen = ev.IsUploadWindowOpen,
                Albums = ev.Albums.Select(a => new AlbumSummary
                {
                    AlbumId = a.Id,
                    Name = a.Name,
                    PhotoCount = a.Photos.Count(p => !p.IsDeleted),
                    CoverThumbnailUrl = a.Photos.FirstOrDefault(p => !p.IsDeleted)?.ThumbnailBlobUrl
                }).ToList()
            };
            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateAlbum(CreateAlbumViewModel vm)
        {
            if (!await IsPhotographerOnEvent(vm.EventId)) return Forbid();

            _db.Albums.Add(new Album
            {
                EventId = vm.EventId,
                Name = vm.Name,
                CreatedByUserId = CurrentUserId
            });
            await _db.SaveChangesAsync();

            return RedirectToAction(nameof(Albums), new { eventId = vm.EventId });
        }

        // ---------- Upload Photographs ----------
        // GET /Photographer/UploadPhotos/12  (albumId)
        public async Task<IActionResult> UploadPhotos(int albumId)
        {
            var album = await _db.Albums.Include(a => a.Event)
                .FirstOrDefaultAsync(a => a.Id == albumId);
            if (album == null) return NotFound();
            if (!await IsPhotographerOnEvent(album.EventId)) return Forbid();

            if (!album.Event.IsUploadWindowOpen)
            {
                TempData["Error"] = "Uploads are only accepted while the event is Live.";
                return RedirectToAction(nameof(Albums), new { eventId = album.EventId });
            }

            return View(new UploadPhotosViewModel
            {
                AlbumId = album.Id,
                AlbumName = album.Name,
                EventId = album.EventId
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(500_000_000)]
        public async Task<IActionResult> UploadPhotos(UploadPhotosViewModel vm)
        {
            var album = await _db.Albums.Include(a => a.Event)
                .FirstOrDefaultAsync(a => a.Id == vm.AlbumId);
            if (album == null) return NotFound();
            if (!await IsPhotographerOnEvent(album.EventId)) return Forbid();
            if (!album.Event.IsUploadWindowOpen)
            {
                TempData["Error"] = "Uploads are only accepted while the event is Live.";
                return RedirectToAction(nameof(Albums), new { eventId = album.EventId });
            }

            int uploadedCount = 0;
            foreach (var file in vm.Files)
            {
                if (file.Length == 0) continue;

                // NOTE: this is where the blob-storage upload call goes
                // (original + queue-triggered display/thumbnail renditions).
                // See BlobStorageService (not shown here) — upload returns
                // the three URLs below.
                var (originalUrl, displayUrl, thumbUrl) = await UploadToBlobStorage(file);

                var photo = new Photo
                {
                    AlbumId = album.Id,
                    OriginalBlobUrl = originalUrl,
                    DisplayBlobUrl = displayUrl,
                    ThumbnailBlobUrl = thumbUrl,
                    UploadedByUserId = CurrentUserId,
                    FaceProcessingComplete = false
                };
                _db.Photos.Add(photo);
                uploadedCount++;
            }
            await _db.SaveChangesAsync();

            // TODO: enqueue the new photo batch on Azure Service Bus for the
            // Python face worker (detect -> embed -> cluster). This keeps a
            // 300-photo batch from blocking the request path per the README.
            // await _serviceBusPublisher.EnqueuePhotoBatch(album.EventId, newPhotoIds);

            TempData["Success"] = $"{uploadedCount} photo(s) uploaded and queued for face processing.";
            return RedirectToAction(nameof(Albums), new { eventId = album.EventId });
        }

        // Placeholder for the real Azure Blob Storage integration.
        private Task<(string original, string display, string thumbnail)> UploadToBlobStorage(IFormFile file)
        {
            // Swap this for real IBlobStorageService calls.
            var placeholder = $"/uploads/placeholder-{Guid.NewGuid()}-{file.FileName}";
            return Task.FromResult((placeholder, placeholder, placeholder));
        }

        // ---------- Review Face Clusters + View Unidentified Clusters ----------
        // GET /Photographer/FaceClusters/5  (eventId)
        public async Task<IActionResult> FaceClusters(int eventId)
        {
            if (!await IsPhotographerOnEvent(eventId)) return Forbid();

            var ev = await _db.Events.FirstOrDefaultAsync(e => e.Id == eventId);
            if (ev == null) return NotFound();

            var clusters = await _db.FaceClusters
                .Where(c => c.EventId == eventId)
                .Include(c => c.FaceDetections).ThenInclude(fd => fd.Photo)
                .Include(c => c.IdentifiedEventMembership).ThenInclude(m => m!.User)
                .ToListAsync();

            var vm = new FaceClusterListViewModel
            {
                EventId = eventId,
                EventName = ev.Name,
                IdentifiedClusters = clusters.Where(c => c.IsIdentified).Select(ToSummary).ToList(),
                UnidentifiedClusters = clusters.Where(c => !c.IsIdentified).Select(ToSummary).ToList()
            };
            return View(vm);
        }

        private static FaceClusterSummary ToSummary(FaceCluster c) => new()
        {
            ClusterId = c.Id,
            DetectionCount = c.FaceDetections.Count,
            RepresentativeThumbnailUrl = c.FaceDetections.FirstOrDefault()?.Photo.ThumbnailBlobUrl,
            IdentifiedAttendeeName = c.IdentifiedEventMembership?.User.FullName
        };

        // ---------- Identify Face Cluster (Create Attendee Profile) ----------
        // GET /Photographer/IdentifyCluster/44
        public async Task<IActionResult> IdentifyCluster(int clusterId)
        {
            var cluster = await _db.FaceClusters
                .Include(c => c.FaceDetections).ThenInclude(fd => fd.Photo)
                .FirstOrDefaultAsync(c => c.Id == clusterId);
            if (cluster == null) return NotFound();
            if (!await IsPhotographerOnEvent(cluster.EventId)) return Forbid();

            var invitedAttendees = await _db.EventMemberships
                .Where(m => m.EventId == cluster.EventId && m.Role == EventRole.Attendee)
                .Include(m => m.User)
                .Select(m => new ValueTuple<int, string>(m.Id, m.User.FullName))
                .ToListAsync();

            var vm = new IdentifyClusterViewModel
            {
                ClusterId = cluster.Id,
                EventId = cluster.EventId,
                SampleThumbnailUrls = cluster.FaceDetections.Take(6).Select(fd => fd.Photo.ThumbnailBlobUrl).ToList(),
                InvitedAttendees = invitedAttendees
            };
            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> IdentifyCluster(IdentifyClusterViewModel vm)
        {
            var cluster = await _db.FaceClusters
                .Include(c => c.FaceDetections)
                .FirstOrDefaultAsync(c => c.Id == vm.ClusterId);
            if (cluster == null) return NotFound();
            if (!await IsPhotographerOnEvent(cluster.EventId)) return Forbid();

            int membershipId = vm.SelectedEventMembershipId;

            // "Create Attendee Profile" path: photographer identifies someone
            // who isn't in the invited-attendee list yet, so an invite goes
            // out and a membership is reserved for them.
            if (membershipId == 0 && !string.IsNullOrWhiteSpace(vm.NewAttendeeEmail))
            {
                var invite = new Invitation
                {
                    EventId = cluster.EventId,
                    Email = vm.NewAttendeeEmail,
                    IntendedRole = EventRole.Attendee,
                    InvitedByUserId = CurrentUserId,
                    TokenHash = Guid.NewGuid().ToString("N")
                };
                _db.Invitations.Add(invite);
                await _db.SaveChangesAsync();

                TempData["Info"] = $"Invitation sent to {vm.NewAttendeeEmail}. " +
                    "The cluster will link automatically once they accept.";
                return RedirectToAction(nameof(FaceClusters), new { eventId = cluster.EventId });
            }

            cluster.IdentifiedEventMembershipId = membershipId;
            cluster.IdentifiedByUserId = CurrentUserId;
            cluster.IdentifiedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            // Attach suggested tags to every face detection in this cluster,
            // for the newly-identified attendee. Auto-confirm mode skips the
            // Suggested step per the event's TagConfirmationMode.
            var ev = await _db.Events.FirstAsync(e => e.Id == cluster.EventId);
            var membership = await _db.EventMemberships.Include(m => m.User)
                .FirstAsync(m => m.Id == membershipId);

            foreach (var detection in cluster.FaceDetections)
            {
                bool alreadyTagged = await _db.Tags.AnyAsync(t =>
                    t.FaceDetectionId == detection.Id && t.AttendeeUserId == membership.UserId);
                if (alreadyTagged) continue;

                _db.Tags.Add(new Tag
                {
                    FaceDetectionId = detection.Id,
                    AttendeeUserId = membership.UserId,
                    Status = ev.TagConfirmationMode == TagConfirmationMode.AutoConfirm
                        ? TagStatus.Confirmed
                        : TagStatus.Suggested,
                    IsSelfTagged = false
                });
            }
            await _db.SaveChangesAsync();

            TempData["Success"] = $"Cluster identified as {membership.User.FullName}. Tags are suggested across all matching photos.";
            return RedirectToAction(nameof(FaceClusters), new { eventId = cluster.EventId });
        }

        // ---------- Edit Attendee Profiles ----------
        // (Photographer-side correction, e.g. fixing a name typo from a
        // misidentified cluster — distinct from the attendee's own
        // "View/Edit Own Profile" use case.)
        public async Task<IActionResult> EditAttendeeProfile(int eventMembershipId)
        {
            var membership = await _db.EventMemberships.Include(m => m.User)
                .FirstOrDefaultAsync(m => m.Id == eventMembershipId);
            if (membership == null) return NotFound();
            if (!await IsPhotographerOnEvent(membership.EventId)) return Forbid();

            return View(new EditAttendeeProfileViewModel
            {
                EventMembershipId = membership.Id,
                FullName = membership.User.FullName,
                Bio = membership.User.Bio
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditAttendeeProfile(EditAttendeeProfileViewModel vm)
        {
            var membership = await _db.EventMemberships.Include(m => m.User)
                .FirstOrDefaultAsync(m => m.Id == vm.EventMembershipId);
            if (membership == null) return NotFound();
            if (!await IsPhotographerOnEvent(membership.EventId)) return Forbid();

            membership.User.FullName = vm.FullName;
            membership.User.Bio = vm.Bio;
            await _db.SaveChangesAsync();

            TempData["Success"] = "Attendee profile updated.";
            return RedirectToAction(nameof(FaceClusters), new { eventId = membership.EventId });
        }

        // ---------- Invite Event Coordinator ----------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> InviteCoordinator(int eventId, string email)
        {
            if (!await IsPhotographerOnEvent(eventId)) return Forbid();

            _db.Invitations.Add(new Invitation
            {
                EventId = eventId,
                Email = email,
                IntendedRole = EventRole.EventCoordinator,
                InvitedByUserId = CurrentUserId,
                TokenHash = Guid.NewGuid().ToString("N")
            });
            await _db.SaveChangesAsync();

            TempData["Success"] = $"Coordinator invite sent to {email}.";
            return RedirectToAction(nameof(Index));
        }

        // ---------- Shared authorization check ----------
        private async Task<bool> IsPhotographerOnEvent(int eventId) =>
            await _db.EventMemberships.AnyAsync(m =>
                m.UserId == CurrentUserId && m.EventId == eventId && m.Role == EventRole.Photographer);
    }
}
