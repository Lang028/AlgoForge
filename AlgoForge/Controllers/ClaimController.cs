using AlgoForge.Data;
using AlgoForge.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AlgoForge.Controllers
{
    // D3: the emailed invite link is how a person claims their Attendee record with a
    // User account. Requires login first (rather than registering inline) so the flow
    // stays simple -- if you're not signed in yet, come back to this link after you are.
    [Authorize]
    public class ClaimController : Controller
    {
        private readonly AlgoForgeDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;

        public ClaimController(AlgoForgeDbContext db, UserManager<ApplicationUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        [HttpGet]
        [Route("Claim/{token}")]
        public async Task<IActionResult> Claim(string token)
        {
            var attendee = await _db.Attendees.FirstOrDefaultAsync(a => a.InviteToken == token);
            if (attendee is null)
            {
                return NotFound("This invite link isn't valid.");
            }

            var userIdText = _userManager.GetUserId(User);
            if (userIdText is null || !Guid.TryParse(userIdText, out var userId))
            {
                return Challenge();
            }

            if (attendee.ClaimedByUserId is null)
            {
                attendee.ClaimedByUserId = userId;
                await _db.SaveChangesAsync();
                TempData["SuccessMessage"] = "You've claimed your invite. Here are your tags.";
            }
            else if (attendee.ClaimedByUserId != userId)
            {
                return Forbid();
            }

            return RedirectToAction("Index", "Tags", new { eventId = attendee.EventId });
        }
    }
}
