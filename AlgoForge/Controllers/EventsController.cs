using AlgoForge.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AlgoForge.Controllers
{
    // Deliberately read-only. Create/edit/invitee-upload screens are separate work --
    // this exists only so there's a way to navigate to an event's photo gallery.
    [Authorize]
    public class EventsController : Controller
    {
        private readonly AlgoForgeDbContext _db;

        public EventsController(AlgoForgeDbContext db)
        {
            _db = db;
        }

        public async Task<IActionResult> Index()
        {
            var events = await _db.Events
                .Include(e => e.Organisation)
                .OrderByDescending(e => e.EventDate)
                .ToListAsync();
            return View(events);
        }
    }
}
