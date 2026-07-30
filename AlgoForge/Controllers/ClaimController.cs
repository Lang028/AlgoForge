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
        private readonly SignInManager<ApplicationUser> _signInManager;

        public ClaimController(
            AlgoForgeDbContext db, UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager)
        {
            _db = db;
            _userManager = userManager;
            _signInManager = signInManager;
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
                TempData["SuccessMessage"] = "You've claimed your invite. Here are your tags.";
            }
            else if (attendee.ClaimedByUserId != userId)
            {
                return Forbid();
            }

            // Claiming is what turns a person record into a member of the event (BUILD_GUIDE
            // §3). Until the access rules went in nothing read EventMembership, so this step
            // was never written and no attendee ever got a membership row -- which now means
            // no gallery. Granted here, and granted on a repeat visit too, so the attendees
            // who claimed before this existed are repaired by following their link again.
            var alreadyMember = await _db.EventMemberships.AnyAsync(
                m => m.EventId == attendee.EventId
                     && m.UserId == userId
                     && m.Role == EventRole.Attendee);

            if (!alreadyMember)
            {
                _db.EventMemberships.Add(new EventMembership
                {
                    Id = Guid.NewGuid(),
                    EventId = attendee.EventId,
                    UserId = userId,
                    Role = EventRole.Attendee
                });
            }

            await _db.SaveChangesAsync();

            // The dashboard and top nav read the ActiveRole claim, not a live membership
            // query (AccountController.ResolveActiveRoleAsync), so without this someone who
            // registered as a Coordinator/Photographer and then claims an attendee invite
            // stays on the staff-facing dashboard forever -- that claim is sticky by design
            // and a plain login never revisits it once it is set to a staff role.
            var user = await _userManager.GetUserAsync(User);
            if (user is not null)
            {
                var existingClaim = (await _userManager.GetClaimsAsync(user))
                    .FirstOrDefault(c => c.Type == "ActiveRole");
                var newClaim = new System.Security.Claims.Claim("ActiveRole", EventRole.Attendee.ToString());
                if (existingClaim is null)
                {
                    await _userManager.AddClaimAsync(user, newClaim);
                    await _signInManager.RefreshSignInAsync(user);
                }
                else if (existingClaim.Value != newClaim.Value)
                {
                    await _userManager.ReplaceClaimAsync(user, existingClaim, newClaim);
                    await _signInManager.RefreshSignInAsync(user);
                }
            }

            return RedirectToAction("Index", "Tags", new { eventId = attendee.EventId });
        }
    }
}
