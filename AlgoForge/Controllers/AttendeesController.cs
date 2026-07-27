using AlgoForge.Data;
using AlgoForge.Models;
using AlgoForge.ViewModels.Attendees;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AlgoForge.Controllers
{
    // Attendee management: Add, Edit, and list. This is really an Event Coordinator
    // use case ("Invite Attendees", "Review & Edit Attendee Profiles" on the use-case
    // diagram), but since EventCoordinatorController doesn't exist yet, access is
    // granted to anyone managing the event (Photographer today, Coordinator once built).
    [Authorize]
    public class AttendeesController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;

        public AttendeesController(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        private string CurrentUserId => _userManager.GetUserId(User)!;

        // GET /Attendees/Index?eventId=5
        public async Task<IActionResult> Index(int eventId)
        {
            if (!await CanManageEvent(eventId)) return Forbid();

            var ev = await _db.Events.FirstOrDefaultAsync(e => e.Id == eventId);
            if (ev == null) return NotFound();

            var attendees = await _db.Attendees
                .Where(a => a.EventId == eventId)
                .OrderBy(a => a.Name)
                .Select(a => new AttendeeRow
                {
                    Id = a.Id,
                    Name = a.Name,
                    Email = a.Email,
                    IsClaimed = a.ClaimedByUserId != null
                })
                .ToListAsync();

            return View(new AttendeeListViewModel
            {
                EventId = eventId,
                EventName = ev.Name,
                Attendees = attendees
            });
        }

        // ---------------- ADD ----------------

        public async Task<IActionResult> Add(int eventId)
        {
            if (!await CanManageEvent(eventId)) return Forbid();

            var ev = await _db.Events.FirstOrDefaultAsync(e => e.Id == eventId);
            if (ev == null) return NotFound();

            return View(new AttendeeFormViewModel
            {
                EventId = eventId,
                EventName = ev.Name,
                IsClaimed = false
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Add(AttendeeFormViewModel model)
        {
            if (!await CanManageEvent(model.EventId)) return Forbid();

            var ev = await _db.Events.FirstOrDefaultAsync(e => e.Id == model.EventId);
            if (ev == null) return NotFound();

            // Rule: one email per event.
            var duplicate = await _db.Attendees.AnyAsync(a =>
                a.EventId == model.EventId && a.Email == model.Email);

            if (duplicate)
            {
                ModelState.AddModelError(nameof(model.Email), "An attendee with this email already exists for this event.");
            }

            if (!ModelState.IsValid)
            {
                model.EventName = ev.Name;
                model.IsClaimed = false;
                return View(model);
            }

            var attendee = new Attendee
            {
                EventId = model.EventId,
                Name = model.Name,
                Email = model.Email,
                ContactInfo = model.ContactInfo,
                // Rule: generate an invite token on create.
                InviteToken = Guid.NewGuid().ToString("N")
            };

            _db.Attendees.Add(attendee);
            await _db.SaveChangesAsync();

            TempData["Success"] = $"{attendee.Name} added.";
            return RedirectToAction(nameof(Index), new { eventId = model.EventId });
        }

        // ---------------- EDIT ----------------

        public async Task<IActionResult> Edit(int attendeeId)
        {
            var attendee = await _db.Attendees.FirstOrDefaultAsync(a => a.Id == attendeeId);
            if (attendee == null) return NotFound();
            if (!await CanManageEvent(attendee.EventId)) return Forbid();

            var ev = await _db.Events.FirstAsync(e => e.Id == attendee.EventId);

            return View(new AttendeeFormViewModel
            {
                EventId = attendee.EventId,
                Id = attendee.Id,
                Name = attendee.Name,
                Email = attendee.Email,
                ContactInfo = attendee.ContactInfo,
                EventName = ev.Name,
                IsClaimed = attendee.ClaimedByUserId != null
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(AttendeeFormViewModel model)
        {
            var attendee = await _db.Attendees.FirstOrDefaultAsync(a => a.Id == model.Id);
            if (attendee == null) return NotFound();
            if (!await CanManageEvent(attendee.EventId)) return Forbid();

            // Rule: one email per event, excluding this attendee's own row.
            var duplicate = await _db.Attendees.AnyAsync(a =>
                a.EventId == attendee.EventId &&
                a.Id != model.Id &&
                a.Email == model.Email);

            if (duplicate)
            {
                ModelState.AddModelError(nameof(model.Email), "An attendee with this email already exists for this event.");
            }

            if (!ModelState.IsValid)
            {
                var ev = await _db.Events.FirstAsync(e => e.Id == attendee.EventId);
                model.EventId = attendee.EventId;
                model.EventName = ev.Name;
                model.IsClaimed = attendee.ClaimedByUserId != null;
                return View(model);
            }

            bool emailChanged = !string.Equals(attendee.Email, model.Email, StringComparison.OrdinalIgnoreCase);
            bool wasUnclaimed = attendee.ClaimedByUserId is null;

            // Assign onto the tracked row -- never build a new Attendee from the form
            // and call _db.Update(), or a crafted post could overwrite InviteToken,
            // ClaimedByUserId, or ContactsVisible.
            attendee.Name = model.Name;
            attendee.Email = model.Email;
            attendee.ContactInfo = model.ContactInfo;

            // Rule: regenerate the token when the email changes on an unclaimed attendee.
            // If they've already claimed, the token is spent and irrelevant to change.
            bool tokenRegenerated = false;
            if (wasUnclaimed && emailChanged)
            {
                attendee.InviteToken = Guid.NewGuid().ToString("N");
                tokenRegenerated = true;
            }

            await _db.SaveChangesAsync();

            TempData["Success"] = tokenRegenerated
                ? $"{attendee.Name} updated. Their email changed, so a new invite link was generated."
                : $"{attendee.Name} updated.";

            return RedirectToAction(nameof(Index), new { eventId = attendee.EventId });
        }

        private async Task<bool> CanManageEvent(int eventId) =>
            await _db.EventMemberships.AnyAsync(m =>
                m.UserId == CurrentUserId && m.EventId == eventId &&
                (m.Role == EventRole.Photographer || m.Role == EventRole.EventCoordinator));
    }
}