using AlgoForge.Data;
using AlgoForge.Models;
using AlgoForge.Services.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AlgoForge.Controllers
{
    // The attendee's own page for one event: their details, and the faces they can claim
    // as themselves.
    //
    // Everything here is scoped to the caller's own claimed Attendee record rather than to
    // an id from the request, which is a stronger check than event membership -- you edit
    // your details and nobody else's, and you may only ever claim a face *as yourself*.
    [Authorize]
    public class MeController : Controller
    {
        private readonly AlgoForgeDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;

        public MeController(AlgoForgeDbContext db, UserManager<ApplicationUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        // GET /Me/{eventId}
        [HttpGet]
        [Route("Me/{eventId}")]
        [RequireEventRole(EventRole.Attendee)]
        public async Task<IActionResult> Index(Guid eventId)
        {
            var attendee = await CurrentAttendeeAsync(eventId);
            if (attendee is null)
            {
                return View("NoAttendeeRecord");
            }

            var evt = await _db.Events.FindAsync(eventId);
            ViewData["EventId"] = eventId;
            ViewData["EventName"] = evt?.Name ?? "Event";

            // Faces the attendee can claim: groups nobody has been linked to yet, and which
            // hold at least one clearly-visible detection. A group already linked to someone
            // else is never offered -- claiming is for unclaimed faces only.
            var unclaimed = await _db.PersonClusters
                .Where(c => c.EventId == eventId
                            && c.LinkedAttendeeId == null
                            && c.Status == PersonClusterStatus.Unidentified)
                .Include(c => c.Detections.Where(d => d.IsTaggable))
                    .ThenInclude(d => d.Photo)
                .ToListAsync();

            ViewData["Unclaimed"] = unclaimed
                .Where(c => c.Detections.Any(d => d.Photo is not null))
                .ToList();

            return View(attendee);
        }

        // POST /Me/{eventId}/Profile -- the attendee edits their own details.
        [HttpPost]
        [Route("Me/{eventId}/Profile")]
        [ValidateAntiForgeryToken]
        [RequireEventRole(EventRole.Attendee)]
        public async Task<IActionResult> Profile(
            Guid eventId, string name, string contactInfo, string about, bool contactsVisible)
        {
            var attendee = await CurrentAttendeeAsync(eventId);
            if (attendee is null)
            {
                return Forbid();
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                TempData["ErrorMessage"] = "Your name can't be blank.";
                return RedirectToAction(nameof(Index), new { eventId });
            }

            // Email is deliberately not editable here. It is what the coordinator's invite
            // was addressed to and what identifies this record; changing it would quietly
            // repoint someone else's invitation.
            attendee.Name = name.Trim();
            attendee.ContactInfo = (contactInfo ?? string.Empty).Trim();
            attendee.About = (about ?? string.Empty).Trim();
            attendee.ContactsVisible = contactsVisible;

            await _db.SaveChangesAsync();

            TempData["SuccessMessage"] = "Your details have been updated.";
            return RedirectToAction(nameof(Index), new { eventId });
        }

        // POST /Me/{eventId}/ThisIsMe -- the attendee recognises themselves in a group of
        // faces. Self-identification, so the tags it mints are Confirmed rather than
        // Suggested: the person consenting and the person being named are the same.
        [HttpPost]
        [Route("Me/{eventId}/ThisIsMe")]
        [ValidateAntiForgeryToken]
        [RequireEventRole(EventRole.Attendee)]
        public async Task<IActionResult> ThisIsMe(Guid eventId, Guid clusterId)
        {
            var attendee = await CurrentAttendeeAsync(eventId);
            if (attendee is null)
            {
                return Forbid();
            }

            var cluster = await _db.PersonClusters
                .Include(c => c.Detections.Where(d => d.IsTaggable))
                .FirstOrDefaultAsync(c => c.Id == clusterId && c.EventId == eventId);

            if (cluster is null)
            {
                return NotFound();
            }

            // Only an unclaimed group. Claiming one already linked to someone would let an
            // attendee overwrite another person's identification.
            if (cluster.LinkedAttendeeId is not null)
            {
                TempData["ErrorMessage"] = "Someone has already been identified in those photos.";
                return RedirectToAction(nameof(Index), new { eventId });
            }

            cluster.LinkedAttendeeId = attendee.Id;
            cluster.Status = PersonClusterStatus.Identified;

            // Non-null because CurrentAttendeeAsync matched on it.
            var userId = attendee.ClaimedByUserId!.Value;
            var added = 0;

            foreach (var detection in cluster.Detections.Where(d => d.IsTaggable))
            {
                var alreadyTagged = await _db.Tags
                    .AnyAsync(t => t.PersonDetectionId == detection.Id && t.TaggedAttendeeId == attendee.Id);

                if (alreadyTagged)
                {
                    continue;
                }

                _db.Tags.Add(new Tag
                {
                    Id = Guid.NewGuid(),
                    PersonDetectionId = detection.Id,
                    TaggedAttendeeId = attendee.Id,
                    // Confirmed on purpose: you are naming yourself, which is the consent
                    // the Suggested state exists to wait for.
                    Status = TagStatus.Confirmed,
                    Origin = TagOrigin.SelfTag,
                    CreatedByUserId = userId,
                    CreatedAt = DateTime.UtcNow,
                    ResolvedAt = DateTime.UtcNow
                });

                added++;
            }

            await _db.SaveChangesAsync();

            TempData["SuccessMessage"] = added == 0
                ? "You were already tagged in those photos."
                : $"Done -- you're now identified in {added} photo{(added == 1 ? "" : "s")}.";

            return RedirectToAction(nameof(Index), new { eventId });
        }

        private async Task<Attendee?> CurrentAttendeeAsync(Guid eventId)
        {
            var userIdText = _userManager.GetUserId(User);
            if (userIdText is null || !Guid.TryParse(userIdText, out var userId))
            {
                return null;
            }

            return await _db.Attendees
                .FirstOrDefaultAsync(a => a.EventId == eventId && a.ClaimedByUserId == userId);
        }
    }
}
