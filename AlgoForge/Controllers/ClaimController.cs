using AlgoForge.Data;
using AlgoForge.Models;
using AlgoForge.Services;
using AlgoForge.ViewModels.Claim;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AlgoForge.Controllers
{
    // D3: the emailed invite link is how a person claims their Attendee record.
    //
    // Deliberately not [Authorize]. The old version required a full password-based sign-in
    // first, which meant a brand-new attendee -- someone who has never used the app and
    // never will again after this event -- had to go through Register (choosing a
    // Coordinator/Photographer role that means nothing to them) before they could see a
    // single photo. The token in the link already proves they're the person the invite was
    // sent to; asking them to also prove it with a password they've never set is friction
    // with no security benefit. So: if they're already signed in, behave as before
    // (auto-claim onto the current account, unchanged for anyone who already has one). If
    // not, show a one-field "confirm your email" page instead of a login wall, and sign
    // them into a passwordless account on a match -- the token plus a correct email is the
    // credential, permanently, for this flow.
    public class ClaimController : Controller
    {
        private readonly AlgoForgeDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IWebHostEnvironment _env;

        public ClaimController(
            AlgoForgeDbContext db,
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            IWebHostEnvironment env)
        {
            _db = db;
            _userManager = userManager;
            _signInManager = signInManager;
            _env = env;
        }

        [HttpGet]
        [Route("Claim/{token}")]
        public async Task<IActionResult> Claim(string token)
        {
            var attendee = await _db.Attendees.Include(a => a.Event).FirstOrDefaultAsync(a => a.InviteToken == token);
            if (attendee is null)
            {
                return View("InviteInvalid");
            }

            var userIdText = _userManager.GetUserId(User);
            if (userIdText is null || !Guid.TryParse(userIdText, out var userId))
            {
                // Not signed in -- the lightweight path. Nothing is granted yet; that only
                // happens once the email below actually matches.
                return View("Verify", new ClaimVerifyViewModel
                {
                    Token = token,
                    EventName = attendee.Event?.Name ?? "this event",
                    InviteUrl = Url.Action(nameof(Claim), "Claim", new { token }, Request.Scheme)!
                });
            }

            if (attendee.ClaimedByUserId is not null && attendee.ClaimedByUserId != userId)
            {
                return Forbid();
            }

            await GrantAndSignInAsync(attendee, userId);
            return RedirectToAction("Index", "Tags", new { eventId = attendee.EventId });
        }

        // POST /Claim/{token}/Verify -- the email confirmation the unauthenticated path above
        // renders. A match signs the visitor into a passwordless account (creating one if
        // this is genuinely their first time) rather than asking them to set a password
        // they'll never be prompted for again.
        [HttpPost]
        [Route("Claim/{token}/Verify")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Verify(string token, string email)
        {
            var attendee = await _db.Attendees.Include(a => a.Event).FirstOrDefaultAsync(a => a.InviteToken == token);
            if (attendee is null)
            {
                return View("InviteInvalid");
            }

            var model = new ClaimVerifyViewModel
            {
                Token = token,
                EventName = attendee.Event?.Name ?? "this event",
                InviteUrl = Url.Action(nameof(Claim), "Claim", new { token }, Request.Scheme)!
            };

            if (!string.Equals(email?.Trim(), attendee.Email, StringComparison.OrdinalIgnoreCase))
            {
                model.Error = "That doesn't match the email this invite was sent to.";
                return View("Verify", model);
            }

            var user = await _userManager.FindByEmailAsync(attendee.Email);
            if (user is null)
            {
                user = new ApplicationUser
                {
                    UserName = attendee.Email,
                    Email = attendee.Email,
                    DisplayName = attendee.Name,
                    EmailConfirmed = true
                };

                // No password: this account's only way in is this same link-plus-email
                // flow, which is the point -- a one-time attendee never sees a password
                // field they'd have no reason to remember.
                var created = await _userManager.CreateAsync(user);
                if (!created.Succeeded)
                {
                    model.Error = "Couldn't create your access -- please try again.";
                    return View("Verify", model);
                }
            }

            await _signInManager.SignInAsync(user, isPersistent: true);
            await GrantAndSignInAsync(attendee, user.Id);

            return RedirectToAction("Index", "Tags", new { eventId = attendee.EventId });
        }

        // GET /Claim/{token}/Photo/{index} -- background imagery for the verify page above.
        // Deliberately not the authorising Photos/File action: nobody is signed in yet at
        // this point. Scoped strictly to the one event this specific token belongs to, which
        // is no wider than what claiming the invite is about to grant anyway.
        [HttpGet]
        [Route("Claim/{token}/Photo/{index:int}")]
        public async Task<IActionResult> Photo(string token, int index)
        {
            var attendee = await _db.Attendees.FirstOrDefaultAsync(a => a.InviteToken == token);
            if (attendee is null)
            {
                return NotFound();
            }

            var photoIds = await _db.Photos
                .Where(p => p.EventId == attendee.EventId && p.Status == PhotoStatus.Visible)
                .OrderBy(p => p.Id) // stable order -- same index means the same photo across the handful of <img> tags on the page
                .Select(p => p.Id)
                .ToListAsync();

            if (photoIds.Count == 0)
            {
                return NotFound();
            }

            var photo = await _db.Photos.FindAsync(photoIds[index % photoIds.Count]);
            var path = photo is null ? null : PhotoStorage.ResolveStoredPath(HttpContext.RequestServices
                .GetRequiredService<IWebHostEnvironment>(), photo);

            if (path is null)
            {
                return NotFound();
            }

            var contentType = Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                ".bmp" => "image/bmp",
                _ => "image/jpeg"
            };

            return PhysicalFile(path, contentType);
        }

        private async Task GrantAndSignInAsync(Attendee attendee, Guid userId)
        {
            if (attendee.ClaimedByUserId is null)
            {
                attendee.ClaimedByUserId = userId;
                TempData["SuccessMessage"] = "You've claimed your invite. Here are your tags.";
            }

            // Claiming is what turns a person record into a member of the event (BUILD_GUIDE
            // §3). Granted on a repeat visit too, so attendees who claimed before this
            // existed are repaired by following their link again.
            var alreadyMember = await _db.EventMemberships.AnyAsync(
                m => m.EventId == attendee.EventId && m.UserId == userId && m.Role == EventRole.Attendee);

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
            var user = await _userManager.FindByIdAsync(userId.ToString());
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
        }
    }
}
