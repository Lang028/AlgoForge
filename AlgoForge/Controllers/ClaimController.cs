using AlgoForge.Data;
using AlgoForge.Models;
using AlgoForge.ViewModels.Account;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AlgoForge.Controllers
{
    // D3: the emailed invite link is how a person claims their Attendee record with a
    // User account.
    //
    // This is deliberately a separate front door from Account/Login. An attendee is not a
    // member of staff having a bad day with their password -- they followed a link to look
    // at photographs of themselves, and sending them to a form that asks whether they are
    // an organisation or a photographer is asking a question they have no way to answer.
    // So the link lands on the album, behind a gate, and the gate opens on an email plus
    // the link itself. No password, no role dropdown, no redirect away from the URL they
    // pasted -- that last part matters, because a link that visibly turns into
    // /Account/Login reads as "wrong link" to the person holding it.
    [AllowAnonymous]
    public class ClaimController : Controller
    {
        private readonly AlgoForgeDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly ILogger<ClaimController> _logger;

        public ClaimController(
            AlgoForgeDbContext db,
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            ILogger<ClaimController> logger)
        {
            _db = db;
            _userManager = userManager;
            _signInManager = signInManager;
            _logger = logger;
        }

        [HttpGet]
        [Route("Claim/{token}")]
        public async Task<IActionResult> Claim(string token)
        {
            var attendee = await _db.Attendees
                .Include(a => a.Event)
                .FirstOrDefaultAsync(a => a.InviteToken == token);

            if (attendee is null)
            {
                return NotFound("This invite link isn't valid.");
            }

            // Already signed in: nothing to ask. Finish the claim and go straight through.
            var userIdText = _userManager.GetUserId(User);
            if (userIdText is not null && Guid.TryParse(userIdText, out var signedInUserId))
            {
                return await CompleteAsync(attendee, signedInUserId);
            }

            // Anonymous: render the album behind the gate. The token travels in the form so
            // the URL never changes, even when the gate comes back with an error.
            return View("Gate", new AttendeeGateViewModel
            {
                Link = Url.Action(nameof(Claim), "Claim", new { token }, Request.Scheme) ?? token,
                EventName = attendee.Event?.Name ?? "this event"
            });
        }

        [HttpPost]
        [Route("Claim/Enter")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Enter(AttendeeGateViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View("Gate", model);
            }

            var token = ExtractToken(model.Link);
            var attendee = token is null
                ? null
                : await _db.Attendees
                    .Include(a => a.Event)
                    .FirstOrDefaultAsync(a => a.InviteToken == token);

            model.EventName = attendee?.Event?.Name ?? string.Empty;

            // One message for every failure below, on purpose. Saying "that email is not on
            // the guest list" would turn this page into a way to test whether a given person
            // attended -- which is exactly the kind of question the invite list is private to
            // prevent anyone from answering.
            if (attendee is null
                || !string.Equals(attendee.Email, model.Email.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Failed attendee gate attempt for token {Token}.", token ?? "(unparseable)");
                ModelState.AddModelError(string.Empty,
                    "That email and link don't match an invitation. Check both and try again.");
                return View("Gate", model);
            }

            var user = await _userManager.FindByEmailAsync(attendee.Email);
            if (user is null)
            {
                user = new ApplicationUser
                {
                    Id = Guid.NewGuid(),
                    UserName = attendee.Email,
                    Email = attendee.Email,
                    EmailConfirmed = true,
                    DisplayName = attendee.Name
                };

                // A long random password nobody is ever told. The account is reachable only
                // through this gate; leaving it passwordless would make it a hole in the
                // staff login instead.
                var created = await _userManager.CreateAsync(user, Guid.NewGuid().ToString("N") + "aA1!");
                if (!created.Succeeded)
                {
                    _logger.LogError("Could not create attendee account for {Email}: {Errors}",
                        attendee.Email, string.Join("; ", created.Errors.Select(e => e.Description)));
                    ModelState.AddModelError(string.Empty,
                        "Something went wrong opening your album. Please try again.");
                    return View("Gate", model);
                }
            }

            var result = await CompleteAsync(attendee, user.Id);

            // Only sign in once the claim has actually succeeded -- Forbid below must not
            // leave someone signed in as a person whose record they failed to claim.
            if (result is ForbidResult)
            {
                ModelState.AddModelError(string.Empty,
                    "This invitation has already been claimed by someone else. "
                    + "Ask the event organiser to send you a new one.");
                return View("Gate", model);
            }

            // Attendees navigate as attendees. Without this the shell opens on whatever the
            // last role claim said, which is how someone ends up looking at a photographer's
            // dashboard when all they wanted was the album.
            //
            // Set before signing in, so the cookie carries it -- and replaced rather than
            // added, or every visit through this gate leaves another ActiveRole claim behind.
            var attendeeClaim = new System.Security.Claims.Claim(
                "ActiveRole", EventRole.Attendee.ToString());
            var existingRoleClaim = (await _userManager.GetClaimsAsync(user))
                .FirstOrDefault(c => c.Type == "ActiveRole");

            if (existingRoleClaim is null)
            {
                await _userManager.AddClaimAsync(user, attendeeClaim);
            }
            else if (existingRoleClaim.Value != attendeeClaim.Value)
            {
                await _userManager.ReplaceClaimAsync(user, existingRoleClaim, attendeeClaim);
            }

            await _signInManager.SignInAsync(user, isPersistent: true);

            return result;
        }

        // Links the attendee record to the user and grants event membership.
        private async Task<IActionResult> CompleteAsync(Attendee attendee, Guid userId)
        {
            if (attendee.ClaimedByUserId is null)
            {
                attendee.ClaimedByUserId = userId;
                TempData["SuccessMessage"] = "You've claimed your invite. Here are your photos.";
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

            // D20: members get the complete gallery, not a cropped-down version of it.
            return RedirectToAction("Index", "Photos", new { eventId = attendee.EventId });
        }

        // Accepts a full pasted URL or a bare token -- people paste what they were sent.
        private static string? ExtractToken(string link)
        {
            var trimmed = link.Trim();
            if (trimmed.Length == 0)
            {
                return null;
            }

            // Tokens are Guid.ToString("N"): 32 hex characters, no separators.
            var candidate = trimmed.TrimEnd('/');
            var lastSegment = candidate[(candidate.LastIndexOf('/') + 1)..];

            // Strip any query string or fragment a mail client may have appended.
            lastSegment = lastSegment.Split('?')[0].Split('#')[0];

            return lastSegment.Length == 32 && lastSegment.All(Uri.IsHexDigit) ? lastSegment : null;
        }
    }
}
