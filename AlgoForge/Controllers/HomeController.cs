using AlgoForge.Data;
using AlgoForge.Models;
using AlgoForge.ViewModels.Home;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AlgoForge.Controllers
{
    public class HomeController : Controller
    {
        private readonly AlgoForgeDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;

        public HomeController(AlgoForgeDbContext db, UserManager<ApplicationUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        // Anonymous visitors get the public landing page; signed-in users get the
        // dashboard, shaped by the role they chose at login and scoped to the events
        // they actually belong to.
        public async Task<IActionResult> Index()
        {
            if (!(User.Identity?.IsAuthenticated ?? false))
            {
                return View("Landing");
            }

            // The platform admin monitors the system from their own console and has no
            // member dashboard -- no memberships, no personal panels.
            if (User.IsInRole(DbInitializer.SystemAdminRole))
            {
                return RedirectToAction(nameof(AdminController.Index), "Admin");
            }

            var userIdText = _userManager.GetUserId(User);
            if (userIdText is null || !Guid.TryParse(userIdText, out var userId))
            {
                return View("Landing");
            }

            EventRole? activeRole = Enum.TryParse<EventRole>(
                User.FindFirst("ActiveRole")?.Value, out var parsedRole) ? parsedRole : null;

            // Admins are scoped by the organisations they administer; everyone else by
            // their event memberships.
            List<Guid> eventIds;
            var myOrganisations = new List<OrganisationRow>();
            if (activeRole == EventRole.Admin)
            {
                myOrganisations = await _db.Organisations
                    .Where(o => o.AdminUserId == userId)
                    .OrderBy(o => o.Name)
                    .Select(o => new OrganisationRow
                    {
                        Id = o.Id,
                        Name = o.Name,
                        ContactEmail = o.ContactEmail,
                        EventCount = o.Events.Count
                    })
                    .ToListAsync();

                var orgIds = myOrganisations.Select(o => o.Id).ToList();
                eventIds = await _db.Events
                    .Where(e => orgIds.Contains(e.OrganisationId))
                    .Select(e => e.Id)
                    .ToListAsync();
            }
            else
            {
                eventIds = await _db.EventMemberships
                    .Where(m => m.UserId == userId)
                    .Select(m => m.EventId)
                    .Distinct()
                    .ToListAsync();
            }

            var today = DateTime.UtcNow.Date;
            var weekStart = today.AddDays(-6);

            var uploadsByDay = await _db.Photos
                .Where(p => eventIds.Contains(p.EventId) && p.UploadedAt >= weekStart)
                .GroupBy(p => p.UploadedAt.Date)
                .Select(g => new { Day = g.Key, Count = g.Count() })
                .ToListAsync();

            // Attendee-side roles see their own tags (the consent queue that belongs to
            // them); coordinators and photographers see the tags across their events.
            var isAttendeeSide = activeRole is EventRole.Attendee or EventRole.Delegate;
            var tags = isAttendeeSide
                ? _db.Tags.Where(t => t.TaggedAttendee!.ClaimedByUserId == userId)
                : _db.Tags.Where(t => eventIds.Contains(t.PersonDetection!.Photo!.EventId));

            var model = new DashboardViewModel
            {
                ActiveRole = activeRole,
                MyOrganisations = myOrganisations,
                UserDisplayName = (await _userManager.GetUserAsync(User))?.DisplayName
                    ?? User.Identity?.Name ?? "there",

                TotalEvents = eventIds.Count,
                TotalPhotos = await _db.Photos.CountAsync(p =>
                    eventIds.Contains(p.EventId) && p.Status == PhotoStatus.Visible),
                TotalAttendees = await _db.Attendees.CountAsync(a => eventIds.Contains(a.EventId)),
                TotalConnections = await _db.Connections.CountAsync(c =>
                    (c.RequesterId == userId || c.ReceiverId == userId)
                    && c.Status == ConnectionStatus.Accepted),
                PendingConnectionRequests = await _db.Connections.CountAsync(c =>
                    c.ReceiverId == userId && c.Status == ConnectionStatus.Pending),

                UploadsLast7Days = Enumerable.Range(0, 7)
                    .Select(offset => weekStart.AddDays(offset))
                    .Select(day => new UploadsPerDay
                    {
                        Day = day,
                        Count = uploadsByDay.FirstOrDefault(u => u.Day == day)?.Count ?? 0
                    })
                    .ToList(),

                RecentAttendees = await _db.Attendees
                    .Where(a => eventIds.Contains(a.EventId))
                    .Include(a => a.Event)
                    .OrderByDescending(a => a.Event!.EventDate)
                    .Take(5)
                    .Select(a => new RecentAttendeeRow
                    {
                        Name = a.Name,
                        EventName = a.Event!.Name,
                        Claimed = a.ClaimedByUserId != null,
                        EventDate = a.Event.EventDate
                    })
                    .ToListAsync(),

                UpcomingEvents = await _db.Events
                    .Where(e => eventIds.Contains(e.Id)
                        && (e.Status == EventStatus.Draft || e.Status == EventStatus.Live))
                    .OrderBy(e => e.EventDate)
                    .Take(4)
                    .Select(e => new UpcomingEventRow
                    {
                        Id = e.Id,
                        Name = e.Name,
                        EventDate = e.EventDate,
                        Status = e.Status
                    })
                    .ToListAsync(),

                TotalTags = await tags.CountAsync(),
                SuggestedTags = await tags.CountAsync(t => t.Status == TagStatus.Suggested),
                ConfirmedTags = await tags.CountAsync(t => t.Status == TagStatus.Confirmed),
                RejectedTags = await tags.CountAsync(t => t.Status == TagStatus.Rejected)
            };

            return View(model);
        }

        public IActionResult About()
        {
            ViewBag.Message = "Consent-based event photography and attendee reconnection.";
            return View();
        }

        public IActionResult Contact()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View();
        }
    }
}
