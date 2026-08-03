using AlgoForge.Data;
using AlgoForge.Models;
using AlgoForge.ViewModels.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AlgoForge.Controllers
{
    // The platform admin's own door and console. The admin monitors what happens on the
    // system -- how many events, uploads, tags, connections -- but is deliberately kept
    // out of event content: no gallery links, no attendee lists, no personal details.
    // RequireEventRole never matches a SystemAdmin (they hold no EventMemberships), so
    // event pages stay closed to them by construction, not by promise.
    [Authorize(Roles = DbInitializer.SystemAdminRole)]
    public class AdminController : Controller
    {
        private readonly AlgoForgeDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;

        public AdminController(
            AlgoForgeDbContext db,
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager)
        {
            _db = db;
            _userManager = userManager;
            _signInManager = signInManager;
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult Login()
        {
            if (User.IsInRole(DbInitializer.SystemAdminRole))
            {
                return RedirectToAction(nameof(Index));
            }
            return View();
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(AdminLoginViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            // Same generic error whether the email is unknown, the password is wrong, or
            // the account simply isn't an admin -- this door doesn't confirm which.
            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user is null || !await _userManager.IsInRoleAsync(user, DbInitializer.SystemAdminRole))
            {
                ModelState.AddModelError(string.Empty, "Invalid admin login attempt.");
                return View(model);
            }

            // Lockout counts here too, but the message stays generic on purpose: this door
            // already refuses to say whether an address exists or merely isn't an admin,
            // and a "locked out" reply would answer that for free.
            var result = await _signInManager.PasswordSignInAsync(user, model.Password, isPersistent: false, lockoutOnFailure: true);
            if (!result.Succeeded)
            {
                ModelState.AddModelError(string.Empty, "Invalid admin login attempt.");
                return View(model);
            }

            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Index()
        {
            var today = DateTime.UtcNow.Date;
            var windowStart = today.AddDays(-13);

            var uploadsByDay = await _db.Photos
                .Where(p => p.UploadedAt >= windowStart)
                .GroupBy(p => p.UploadedAt.Date)
                .Select(g => new { Day = g.Key, Count = g.Count() })
                .ToListAsync();

            var model = new AdminDashboardViewModel
            {
                TotalUsers = await _db.Users.CountAsync(),
                TotalOrganisations = await _db.Organisations.CountAsync(),
                TotalEvents = await _db.Events.CountAsync(),
                TotalPhotos = await _db.Photos.CountAsync(),
                TotalAttendees = await _db.Attendees.CountAsync(),
                TotalConnections = await _db.Connections.CountAsync(c => c.Status == ConnectionStatus.Accepted),
                PendingConnections = await _db.Connections.CountAsync(c => c.Status == ConnectionStatus.Pending),

                TotalTags = await _db.Tags.CountAsync(),
                SuggestedTags = await _db.Tags.CountAsync(t => t.Status == TagStatus.Suggested),
                ConfirmedTags = await _db.Tags.CountAsync(t => t.Status == TagStatus.Confirmed),
                RejectedTags = await _db.Tags.CountAsync(t => t.Status == TagStatus.Rejected),

                UploadsLast14Days = Enumerable.Range(0, 14)
                    .Select(offset => windowStart.AddDays(offset))
                    .Select(day => new AdminUploadsPerDay
                    {
                        Day = day,
                        Count = uploadsByDay.FirstOrDefault(u => u.Day == day)?.Count ?? 0
                    })
                    .ToList(),

                Events = await _db.Events
                    .OrderByDescending(e => e.EventDate)
                    .Select(e => new EventOverviewRow
                    {
                        Name = e.Name,
                        OrganisationName = e.Organisation != null ? e.Organisation.Name : "—",
                        EventDate = e.EventDate,
                        Status = e.Status,
                        PhotoCount = e.Photos.Count,
                        AttendeeCount = e.Attendees.Count,
                        TagCount = _db.Tags.Count(t => t.PersonDetection!.Photo!.EventId == e.Id)
                    })
                    .ToListAsync()
            };

            return View(model);
        }
    }
}
