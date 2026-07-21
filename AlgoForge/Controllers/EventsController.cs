using AlgoForge.Data;
using AlgoForge.Models;
using AlgoForge.Services;
using AlgoForge.ViewModels.Events;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AlgoForge.Controllers
{
    [Authorize]
    public class EventsController : Controller
    {
        private readonly AlgoForgeDbContext _db;
        private readonly AttendeeImportService _importService;

        public EventsController(AlgoForgeDbContext db, AttendeeImportService importService)
        {
            _db = db;
            _importService = importService;
        }

        public async Task<IActionResult> Index()
        {
            var events = await _db.Events
                .Include(e => e.Organisation)
                .OrderByDescending(e => e.EventDate)
                .ToListAsync();
            return View(events);
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
