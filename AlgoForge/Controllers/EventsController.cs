using AlgoForge.Data;
using AlgoForge.Models;
using AlgoForge.Services;
using AlgoForge.Services.Authorization;
using AlgoForge.ViewModels.Events;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AlgoForge.Controllers
{
    [Authorize]
    public class EventsController : Controller
    {
        private readonly AlgoForgeDbContext _db;
        private readonly AttendeeImportService _importService;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IEmailSender _emailSender;

        public EventsController(
            AlgoForgeDbContext db,
            AttendeeImportService importService,
            UserManager<ApplicationUser> userManager,
            IEmailSender emailSender)
        {
            _db = db;
            _importService = importService;
            _userManager = userManager;
            _emailSender = emailSender;
        }

        // Only the events this user actually has standing in. The previous version listed
        // every event in the database to every signed-in user, which also handed out the
        // event ids that the rest of the app keyed off.
        public async Task<IActionResult> Index()
        {
            var userIdText = _userManager.GetUserId(User);
            if (userIdText is null || !Guid.TryParse(userIdText, out var userId))
            {
                return Challenge();
            }

            var eventIds = await _db.EventMemberships
                .Where(m => m.UserId == userId)
                .Select(m => m.EventId)
                .Distinct()
                .ToListAsync();

            var events = await _db.Events
                .Where(e => eventIds.Contains(e.Id))
                .Include(e => e.Organisation)
                .OrderByDescending(e => e.EventDate)
                .ToListAsync();

            return View(events);
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            ViewData["Organisations"] = await _db.Organisations.OrderBy(o => o.Name).ToListAsync();
            return View(new CreateEventViewModel());
        }

        // Creating an event grants the creator BOTH memberships, Coordinator and
        // Photographer.
        //
        // D1 keeps those permissions disjoint -- a Coordinator cannot upload photos -- but a
        // photographer running a small event alone has to do both jobs. The answer is not to
        // weaken D1 or invent a fifth role: one person simply holds two membership rows, and
        // the access resolver returns a set. Without this the creator of an event could not
        // upload a single photo to it.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreateEventViewModel model)
        {
            if (!ModelState.IsValid)
            {
                ViewData["Organisations"] = await _db.Organisations.OrderBy(o => o.Name).ToListAsync();
                return View(model);
            }

            var userIdText = _userManager.GetUserId(User);
            if (userIdText is null || !Guid.TryParse(userIdText, out var userId))
            {
                return Challenge();
            }

            var evt = new Event
            {
                Id = Guid.NewGuid(),
                Name = model.Name,
                EventDate = model.EventDate,
                OrganisationId = model.OrganisationId,
                TagConfirmationRequired = model.TagConfirmationRequired,
                Status = EventStatus.Draft
            };
            _db.Events.Add(evt);

            foreach (var role in new[] { EventRole.Coordinator, EventRole.Photographer })
            {
                _db.EventMemberships.Add(new EventMembership
                {
                    Id = Guid.NewGuid(),
                    EventId = evt.Id,
                    UserId = userId,
                    Role = role
                });
            }

            await _db.SaveChangesAsync();

            TempData["SuccessMessage"] = $"{evt.Name} created.";
            return RedirectToAction(nameof(Index));
        }

        // GET /Events/{eventId}/Attendees
        //
        // Coordinator-only, here and on every action below it: D3/D4 make the invitee list
        // the coordinator's to own, and it is the densest concentration of personal data in
        // the app -- names, emails and contact details for people who mostly do not have
        // accounts yet and have consented to nothing.
        [HttpGet]
        [Route("Events/{eventId}/Attendees")]
        [RequireEventRole(EventRole.Coordinator)]
        public async Task<IActionResult> Attendees(Guid eventId)
        {
            var evt = await _db.Events.FindAsync(eventId);
            if (evt is null)
            {
                return NotFound();
            }

            var attendees = await _db.Attendees
                .Where(a => a.EventId == eventId)
                .OrderBy(a => a.Name)
                .ToListAsync();

            ViewData["EventId"] = eventId;
            ViewData["EventName"] = evt.Name;
            return View(attendees);
        }

        // GET /Events/{eventId}/Attendees/Add
        //
        // Manual add for the coordinator, alongside the CSV import: a walk-in or a
        // late addition shouldn't require re-uploading a spreadsheet.
        [HttpGet]
        [Route("Events/{eventId}/Attendees/Add")]
        [RequireEventRole(EventRole.Coordinator)]
        public async Task<IActionResult> AddAttendee(Guid eventId)
        {
            var evt = await _db.Events.FindAsync(eventId);
            if (evt is null)
            {
                return NotFound();
            }

            return View(new AttendeeFormViewModel
            {
                EventId = eventId,
                EventName = evt.Name
            });
        }

        // POST /Events/{eventId}/Attendees/Add
        [HttpPost]
        [Route("Events/{eventId}/Attendees/Add")]
        [ValidateAntiForgeryToken]
        [RequireEventRole(EventRole.Coordinator)]
        public async Task<IActionResult> AddAttendee(Guid eventId, AttendeeFormViewModel model)
        {
            var evt = await _db.Events.FindAsync(eventId);
            if (evt is null)
            {
                return NotFound();
            }

            // Rule: one email per event.
            var duplicate = await _db.Attendees.AnyAsync(a =>
                a.EventId == eventId && a.Email == model.Email);
            if (duplicate)
            {
                ModelState.AddModelError(nameof(model.Email),
                    "An attendee with this email already exists for this event.");
            }

            if (!ModelState.IsValid)
            {
                model.EventId = eventId;
                model.EventName = evt.Name;
                return View(model);
            }

            var attendee = new Attendee
            {
                Id = Guid.NewGuid(),
                EventId = eventId,
                Name = model.Name,
                Email = model.Email,
                ContactInfo = model.ContactInfo,
                InviteToken = Guid.NewGuid().ToString("N")
            };

            _db.Attendees.Add(attendee);
            await _db.SaveChangesAsync();

            TempData["SuccessMessage"] = $"{attendee.Name} added.";
            return RedirectToAction(nameof(Attendees), new { eventId });
        }

        // GET /Events/{eventId}/Attendees/{attendeeId}/Edit
        [HttpGet]
        [Route("Events/{eventId}/Attendees/{attendeeId}/Edit")]
        [RequireEventRole(EventRole.Coordinator)]
        public async Task<IActionResult> EditAttendee(Guid eventId, Guid attendeeId)
        {
            var attendee = await _db.Attendees.FirstOrDefaultAsync(
                a => a.Id == attendeeId && a.EventId == eventId);
            if (attendee is null)
            {
                return NotFound();
            }

            var evt = await _db.Events.FindAsync(eventId);
            if (evt is null)
            {
                return NotFound();
            }

            return View(new AttendeeFormViewModel
            {
                EventId = eventId,
                AttendeeId = attendee.Id,
                Name = attendee.Name,
                Email = attendee.Email,
                ContactInfo = attendee.ContactInfo,
                EventName = evt.Name,
                IsClaimed = attendee.ClaimedByUserId is not null
            });
        }

        // POST /Events/{eventId}/Attendees/{attendeeId}/Edit
        [HttpPost]
        [Route("Events/{eventId}/Attendees/{attendeeId}/Edit")]
        [ValidateAntiForgeryToken]
        [RequireEventRole(EventRole.Coordinator)]
        public async Task<IActionResult> EditAttendee(Guid eventId, Guid attendeeId, AttendeeFormViewModel model)
        {
            // Identity comes from the route, never the form body, so a crafted post
            // can't retarget another event's attendee.
            var attendee = await _db.Attendees.FirstOrDefaultAsync(
                a => a.Id == attendeeId && a.EventId == eventId);
            if (attendee is null)
            {
                return NotFound();
            }

            // Rule: one email per event, excluding this attendee's own row.
            var duplicate = await _db.Attendees.AnyAsync(a =>
                a.EventId == eventId && a.Id != attendeeId && a.Email == model.Email);
            if (duplicate)
            {
                ModelState.AddModelError(nameof(model.Email),
                    "An attendee with this email already exists for this event.");
            }

            if (!ModelState.IsValid)
            {
                var evt = await _db.Events.FindAsync(eventId);
                model.EventId = eventId;
                model.AttendeeId = attendeeId;
                model.EventName = evt?.Name ?? string.Empty;
                model.IsClaimed = attendee.ClaimedByUserId is not null;
                return View(model);
            }

            bool emailChanged = !string.Equals(attendee.Email, model.Email, StringComparison.OrdinalIgnoreCase);
            bool wasUnclaimed = attendee.ClaimedByUserId is null;

            // Assign onto the tracked row -- never attach a form-built Attendee, or a
            // crafted post could overwrite InviteToken, ClaimedByUserId, or ContactsVisible.
            attendee.Name = model.Name;
            attendee.Email = model.Email;
            attendee.ContactInfo = model.ContactInfo;

            // Rule: an unclaimed attendee's invite token is only as trustworthy as the
            // email it was sent to, so a changed email invalidates the old link. Once
            // claimed, the token is spent and there is nothing to rotate.
            bool tokenRegenerated = false;
            if (wasUnclaimed && emailChanged)
            {
                attendee.InviteToken = Guid.NewGuid().ToString("N");
                tokenRegenerated = true;
            }

            await _db.SaveChangesAsync();

            TempData["SuccessMessage"] = tokenRegenerated
                ? $"{attendee.Name} updated. Their email changed, so a new invite link was generated."
                : $"{attendee.Name} updated.";

            return RedirectToAction(nameof(Attendees), new { eventId });
        }

        // Sends (logs, in dev mode -- see NoOpEmailSender) the claim link for one attendee
        // and surfaces it in TempData too, so the demo doesn't depend on reading server logs.
        [HttpPost]
        [Route("Events/{eventId}/Attendees/{attendeeId}/SendInvite")]
        [ValidateAntiForgeryToken]
        [RequireEventRole(EventRole.Coordinator)]
        public async Task<IActionResult> SendInvite(Guid eventId, Guid attendeeId)
        {
            var attendee = await _db.Attendees.FirstOrDefaultAsync(a => a.Id == attendeeId && a.EventId == eventId);
            if (attendee is null)
            {
                return NotFound();
            }

            var claimUrl = Url.Action(nameof(ClaimController.Claim), "Claim", new { token = attendee.InviteToken }, Request.Scheme)!;
            await _emailSender.SendEmailAsync(attendee.Email, "You're tagged in photos from the event",
                $"Claim your photos and review your tags: {claimUrl}");

            TempData["SuccessMessage"] = $"Invite sent to {attendee.Name}. Claim link (dev mode, not actually emailed): {claimUrl}";
            return RedirectToAction(nameof(Attendees), new { eventId });
        }

        // GET /Events/{eventId}/ImportAttendees
        [HttpGet]
        [Route("Events/{eventId}/ImportAttendees")]
        [RequireEventRole(EventRole.Coordinator)]
        public async Task<IActionResult> ImportAttendees(Guid eventId)
        {
            var evt = await _db.Events.FindAsync(eventId);
            if (evt is null)
                return NotFound();

            return View(new ImportAttendeesViewModel { EventId = eventId, EventName = evt.Name });
        }

        // POST /Events/{eventId}/ImportAttendees
        // Parses the uploaded CSV and either re-renders with errors or saves all valid rows.
        [HttpPost]
        [Route("Events/{eventId}/ImportAttendees")]
        [ValidateAntiForgeryToken]
        [RequireEventRole(EventRole.Coordinator)]
        public async Task<IActionResult> ImportAttendees(Guid eventId, IFormFile? file)
        {
            var evt = await _db.Events.FindAsync(eventId);
            if (evt is null)
                return NotFound();

            var vm = new ImportAttendeesViewModel { EventId = eventId, EventName = evt.Name };

            if (file is null || file.Length == 0)
            {
                ModelState.AddModelError(string.Empty, "Please select a CSV file to upload.");
                return View(vm);
            }

            if (file.Length > 10 * 1024 * 1024)
            {
                ModelState.AddModelError(string.Empty, "File size must be 10 MB or less.");
                return View(vm);
            }

            AttendeeImportResult parseResult;
            using (var stream = file.OpenReadStream())
            {
                parseResult = _importService.ParseCsv(stream, eventId);
            }

            vm.ParseResult = parseResult;

            // If any rows have errors, show the summary and let the coordinator decide.
            if (parseResult.Errors.Any())
            {
                return View(vm);
            }

            // All rows valid — save immediately.
            return await SaveAttendees(parseResult.ValidAttendees, parseResult.ValidAttendees.Count, eventId);
        }

        // POST /Events/{eventId}/ImportAttendeesConfirm
        // Called when the coordinator chooses "Import valid rows anyway" after seeing errors.
        // The valid attendees are passed as hidden form fields to avoid storing state server-side.
        [HttpPost]
        [Route("Events/{eventId}/ImportAttendeesConfirm")]
        [ValidateAntiForgeryToken]
        [RequireEventRole(EventRole.Coordinator)]
        public async Task<IActionResult> ImportAttendeesConfirm(Guid eventId,
            [FromForm] List<AttendeeInputModel> attendees)
        {
            var evt = await _db.Events.FindAsync(eventId);
            if (evt is null)
                return NotFound();

            if (attendees is null || attendees.Count == 0)
            {
                TempData["ErrorMessage"] = "No attendees to import.";
                return RedirectToAction(nameof(ImportAttendees), new { eventId });
            }

            var records = attendees.Select(a => new Attendee
            {
                Id = Guid.NewGuid(),
                EventId = eventId,
                Name = a.Name,
                Email = a.Email,
                ContactInfo = a.ContactInfo,
                InviteToken = Guid.NewGuid().ToString("N")
            }).ToList();

            return await SaveAttendees(records, records.Count, eventId);
        }

        private async Task<IActionResult> SaveAttendees(List<Attendee> attendees, int count, Guid eventId)
        {
            foreach (var a in attendees)
            {
                if (string.IsNullOrEmpty(a.InviteToken))
                    a.InviteToken = Guid.NewGuid().ToString("N");
            }

            _db.Attendees.AddRange(attendees);
            await _db.SaveChangesAsync();

            TempData["SuccessMessage"] = $"{count} attendee(s) imported successfully.";
            return RedirectToAction(nameof(Index));
        }
    }
}
