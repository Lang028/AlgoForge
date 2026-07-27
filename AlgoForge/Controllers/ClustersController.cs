using AlgoForge.Data;
using AlgoForge.Models;
using AlgoForge.Services.Authorization;
using AlgoForge.Services.PersonPipeline;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AlgoForge.Controllers
{
    // Coordinator/photographer-facing: link an unidentified PersonCluster to an Attendee.
    // Identifying a cluster is what turns "Person 3" into a real person and is the trigger
    // for Suggested tags (D6).
    //
    // This is also the only place in the app where unconfirmed identities are visible at
    // all -- the gallery stays consent-clean (D20), so identification work happens here.
    //
    // Every action is coordinator/photographer-only (D4: the coordinator is the primary
    // identifier, the photographer keeps it as backup). The gate is on the controller
    // because there is no action here an attendee should ever reach -- identifying a
    // cluster mints Suggested tags against a real person, which is the one thing in the
    // app that must never be reachable by someone with no standing in the event.
    [Authorize]
    [RequireEventRole(EventRole.Coordinator, EventRole.Photographer)]
    public class ClustersController : Controller
    {
        private readonly AlgoForgeDbContext _db;
        private readonly PersonPipelineService _pipeline;
        private readonly UserManager<ApplicationUser> _userManager;

        public ClustersController(
            AlgoForgeDbContext db,
            PersonPipelineService pipeline,
            UserManager<ApplicationUser> userManager)
        {
            _db = db;
            _pipeline = pipeline;
            _userManager = userManager;
        }

        [HttpGet]
        [Route("Clusters/{eventId}")]
        public async Task<IActionResult> Index(Guid eventId)
        {
            var evt = await _db.Events.FindAsync(eventId);
            if (evt is null)
            {
                return NotFound();
            }

            // HasTaggableDetection is the surfacing rule from plan 5.2, applied even here:
            // a cluster built purely from Tier B detections -- someone who never faced the
            // camera, venue staff shot from behind -- is not offered for identification.
            // Letting a photographer name one would create exactly the outcome the gate
            // exists to prevent, just with an extra click in front of it.
            var clusters = await _db.PersonClusters
                .Where(c => c.EventId == eventId
                            && c.HasTaggableDetection
                            && c.Status != PersonClusterStatus.Ignored)
                .Include(c => c.LinkedAttendee)
                .Include(c => c.Detections.Where(d => d.IsTaggable))
                    .ThenInclude(d => d.Photo)
                .OrderBy(c => c.Status)
                .ThenByDescending(c => c.Detections.Count)
                .ToListAsync();

            var attendees = await _db.Attendees
                .Where(a => a.EventId == eventId)
                .OrderBy(a => a.Name)
                .ToListAsync();

            ViewData["EventId"] = eventId;
            ViewData["EventName"] = evt.Name;
            ViewData["Attendees"] = attendees;
            return View(clusters);
        }

        // Links the cluster to an attendee and suggests a tag for every Tier A detection
        // in it. Tag creation itself lives in the pipeline service, which is where the
        // IsTaggable invariant is enforced -- this action decides who, not what is
        // eligible.
        [HttpPost]
        [Route("Clusters/{eventId}/Identify")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Identify(Guid eventId, Guid clusterId, Guid attendeeId)
        {
            var cluster = await _db.PersonClusters
                .FirstOrDefaultAsync(c => c.Id == clusterId && c.EventId == eventId);
            var attendee = await _db.Attendees
                .FirstOrDefaultAsync(a => a.Id == attendeeId && a.EventId == eventId);

            if (cluster is null || attendee is null)
            {
                return NotFound();
            }

            if (!cluster.HasTaggableDetection)
            {
                // Defence in depth: the grid never offers these, so reaching here means a
                // hand-crafted request rather than a mis-click.
                return BadRequest("This cluster has no clearly visible detections and cannot be identified.");
            }

            var userIdText = _userManager.GetUserId(User);
            if (userIdText is null || !Guid.TryParse(userIdText, out var userId))
            {
                return Challenge();
            }

            cluster.LinkedAttendeeId = attendee.Id;
            cluster.Status = PersonClusterStatus.Identified;
            cluster.IdentifiedByUserId = userId;
            await _db.SaveChangesAsync();

            var created = await _pipeline.SyncSuggestedTagsAsync(eventId, cluster.Id, attendee.Id, userId);

            TempData["SuccessMessage"] = $"Cluster linked to {attendee.Name} -- {created} tag(s) suggested.";
            return RedirectToAction(nameof(Index), new { eventId });
        }

        // Marks a cluster as not-an-attendee (staff, passers-by). Hidden from every flow;
        // embeddings fall under the archive-time cleanup (D7).
        [HttpPost]
        [Route("Clusters/{eventId}/Ignore")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Ignore(Guid eventId, Guid clusterId)
        {
            var cluster = await _db.PersonClusters
                .FirstOrDefaultAsync(c => c.Id == clusterId && c.EventId == eventId);
            if (cluster is null)
            {
                return NotFound();
            }

            cluster.Status = PersonClusterStatus.Ignored;
            await _db.SaveChangesAsync();

            TempData["SuccessMessage"] = "Cluster ignored.";
            return RedirectToAction(nameof(Index), new { eventId });
        }

        // Manual re-cluster. Reprocesses every detection in the event from scratch;
        // identified clusters keep their Id and their attendee link (OPEN-4).
        [HttpPost]
        [Route("Clusters/{eventId}/Recluster")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Recluster(Guid eventId)
        {
            var evt = await _db.Events.FindAsync(eventId);
            if (evt is null)
            {
                return NotFound();
            }

            try
            {
                var count = await _pipeline.ReclusterEventAsync(eventId);
                TempData["SuccessMessage"] = $"Re-clustered: {count} cluster(s).";
            }
            catch (HttpRequestException)
            {
                TempData["ErrorMessage"] =
                    "Could not reach the person pipeline service. Start it with: uvicorn main:app --port 8000";
            }

            return RedirectToAction(nameof(Index), new { eventId });
        }
    }
}
