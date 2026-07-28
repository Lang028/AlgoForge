using AlgoForge.Data;
using AlgoForge.Models;
using AlgoForge.ViewModels.Home;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AlgoForge.Controllers
{
    public class HomeController : Controller
    {
        private readonly AlgoForgeDbContext _db;

        public HomeController(AlgoForgeDbContext db)
        {
            _db = db;
        }

        public async Task<IActionResult> Index()
        {
            var today = DateTime.UtcNow.Date;
            var weekStart = today.AddDays(-6);

            var uploadsByDay = await _db.Photos
                .Where(p => p.UploadedAt >= weekStart)
                .GroupBy(p => p.UploadedAt.Date)
                .Select(g => new { Day = g.Key, Count = g.Count() })
                .ToListAsync();

            var model = new DashboardViewModel
            {
                TotalEvents = await _db.Events.CountAsync(),
                TotalPhotos = await _db.Photos.CountAsync(),
                TotalAttendees = await _db.Attendees.CountAsync(),
                TotalConnections = await _db.Connections
                    .CountAsync(c => c.Status == ConnectionStatus.Accepted),

                UploadsLast7Days = Enumerable.Range(0, 7)
                    .Select(offset => weekStart.AddDays(offset))
                    .Select(day => new UploadsPerDay
                    {
                        Day = day,
                        Count = uploadsByDay.FirstOrDefault(u => u.Day == day)?.Count ?? 0
                    })
                    .ToList(),

                RecentAttendees = await _db.Attendees
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
                    .Where(e => e.Status == EventStatus.Draft || e.Status == EventStatus.Live)
                    .OrderBy(e => e.EventDate)
                    .Take(4)
                    .Select(e => new UpcomingEventRow
                    {
                        Name = e.Name,
                        EventDate = e.EventDate,
                        Status = e.Status
                    })
                    .ToListAsync(),

                TotalTags = await _db.Tags.CountAsync(),
                SuggestedTags = await _db.Tags.CountAsync(t => t.Status == TagStatus.Suggested),
                ConfirmedTags = await _db.Tags.CountAsync(t => t.Status == TagStatus.Confirmed),
                RejectedTags = await _db.Tags.CountAsync(t => t.Status == TagStatus.Rejected)
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
