using AlgoForge.Data;
using AlgoForge.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AlgoForge.Controllers
{
    // Attendee-facing: review and confirm/reject the tags suggested against you (D6).
    // This is the consent boundary -- every action here is scoped to the logged-in user's
    // own claimed Attendee record, never to an Attendee id taken from the request.
    [Authorize]
    public class TagsController : Controller
    {
        private readonly AlgoForgeDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;

        public TagsController(AlgoForgeDbContext db, UserManager<ApplicationUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        [HttpGet]
        [Route("Tags/{eventId}")]
        public async Task<IActionResult> Index(Guid eventId)
        {
            var evt = await _db.Events.FindAsync(eventId);
            if (evt is null)
            {
                return NotFound();
            }

            var userId = await CurrentUserIdAsync();
            if (userId is null)
            {
                return Challenge();
            }

            var attendee = await _db.Attendees
                .FirstOrDefaultAsync(a => a.EventId == eventId && a.ClaimedByUserId == userId);

            ViewData["EventId"] = eventId;
            ViewData["EventName"] = evt.Name;

            if (attendee is null)
            {
                ViewData["NoAttendeeRecord"] = true;
                return View(new List<Tag>());
            }

            var tags = await _db.Tags
                .Where(t => t.TaggedAttendeeId == attendee.Id)
                .Include(t => t.PersonDetection)
                    .ThenInclude(d => d!.Photo)
                .OrderBy(t => t.Status)
                .ThenByDescending(t => t.CreatedAt)
                .ToListAsync();

            return View(tags);
        }

        [HttpPost]
        [Route("Tags/{eventId}/Confirm/{tagId}")]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> Confirm(Guid eventId, Guid tagId) => Resolve(eventId, tagId, TagStatus.Confirmed);

        [HttpPost]
        [Route("Tags/{eventId}/Reject/{tagId}")]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> Reject(Guid eventId, Guid tagId) => Resolve(eventId, tagId, TagStatus.Rejected);

        private async Task<IActionResult> Resolve(Guid eventId, Guid tagId, TagStatus newStatus)
        {
            var userId = await CurrentUserIdAsync();
            if (userId is null)
            {
                return Challenge();
            }

            var tag = await _db.Tags
                .Include(t => t.TaggedAttendee)
                .FirstOrDefaultAsync(t => t.Id == tagId);

            // Ownership check: only the person the tag is about can confirm or reject it.
            if (tag is null || tag.TaggedAttendee is null || tag.TaggedAttendee.ClaimedByUserId != userId)
            {
                return Forbid();
            }

            tag.Status = newStatus;
            tag.ResolvedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return RedirectToAction(nameof(Index), new { eventId });
        }

        private async Task<Guid?> CurrentUserIdAsync()
        {
            var userIdText = _userManager.GetUserId(User);
            return userIdText is not null && Guid.TryParse(userIdText, out var userId) ? userId : null;
        }
    }
}
