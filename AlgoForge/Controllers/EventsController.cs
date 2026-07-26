using AlgoForge.Data;
using AlgoForge.Models;
using AlgoForge.Services;
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

        public async Task<IActionResult> Index()
        {
            var events = await _db.Events
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

        // Creating an event also grants the creator a Coordinator membership -- for this
        // build there's no separate "invite a coordinator" step, so whoever stands the
        // event up owns it (matches D4: coordinator is the primary identifier).
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

            _db.EventMemberships.Add(new EventMembership
            {
                Id = Guid.NewGuid(),
                EventId = evt.Id,
                UserId = userId,
                Role = EventRole.Coordinator
            });

            await _db.SaveChangesAsync();

            TempData["SuccessMessage"] = $"{evt.Name} created.";
            return RedirectToAction(nameof(Index));
        }

        // GET /Events/{eventId}/Attendees
        [HttpGet]
        [Route("Events/{eventId}/Attendees")]
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

        // Sends (logs, in dev mode -- see NoOpEmailSender) the claim link for one attendee
        // and surfaces it in TempData too, so the demo doesn't depend on reading server logs.
        [HttpPost]
        [Route("Events/{eventId}/Attendees/{attendeeId}/SendInvite")]
        [ValidateAntiForgeryToken]
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
