using AlgoForge.Data;
using AlgoForge.Models;
using AlgoForge.Services;
using AlgoForge.Services.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AlgoForge.Controllers
{
    // Handing an event over to an organisation.
    //
    // The coordinator invites an email address; whoever accepts creates the organisation
    // themselves and becomes its admin. The photographer never types someone else's
    // organisation details in on their behalf, and never gains the power to create
    // organisations at will.
    [Authorize]
    public class HandoverController : Controller
    {
        private readonly AlgoForgeDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IEmailSender _emailSender;

        public HandoverController(
            AlgoForgeDbContext db,
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            IEmailSender emailSender)
        {
            _db = db;
            _userManager = userManager;
            _signInManager = signInManager;
            _emailSender = emailSender;
        }

        // GET /Handover/{eventId}
        [HttpGet]
        [Route("Handover/{eventId}")]
        [RequireEventRole(EventRole.Coordinator)]
        public async Task<IActionResult> Index(Guid eventId)
        {
            var evt = await _db.Events
                .Include(e => e.Organisation)
                .FirstOrDefaultAsync(e => e.Id == eventId);

            if (evt is null)
            {
                return NotFound();
            }

            ViewData["EventId"] = eventId;
            ViewData["EventName"] = evt.Name;
            ViewData["OrganisationName"] = evt.Organisation?.Name;

            var invites = await _db.OrganisationHandovers
                .Where(h => h.EventId == eventId && !h.Revoked)
                .OrderByDescending(h => h.CreatedAt)
                .ToListAsync();

            return View(invites);
        }

        // POST /Handover/{eventId}/Invite
        [HttpPost]
        [Route("Handover/{eventId}/Invite")]
        [ValidateAntiForgeryToken]
        [RequireEventRole(EventRole.Coordinator)]
        public async Task<IActionResult> Invite(Guid eventId, string email)
        {
            var evt = await _db.Events.FindAsync(eventId);
            if (evt is null)
            {
                return NotFound();
            }

            if (evt.OrganisationId is not null)
            {
                TempData["ErrorMessage"] = "This event already belongs to an organisation.";
                return RedirectToAction(nameof(Index), new { eventId });
            }

            if (string.IsNullOrWhiteSpace(email))
            {
                TempData["ErrorMessage"] = "Enter the email address to invite.";
                return RedirectToAction(nameof(Index), new { eventId });
            }

            var userIdText = _userManager.GetUserId(User);
            if (userIdText is null || !Guid.TryParse(userIdText, out var userId))
            {
                return Challenge();
            }

            var invite = new OrganisationHandover
            {
                Id = Guid.NewGuid(),
                EventId = eventId,
                Email = email.Trim(),
                Token = Guid.NewGuid().ToString("N"),
                InvitedByUserId = userId
            };

            _db.OrganisationHandovers.Add(invite);
            await _db.SaveChangesAsync();

            var acceptUrl = Url.Action(nameof(Accept), "Handover",
                new { token = invite.Token }, Request.Scheme)!;

            await _emailSender.SendEmailAsync(invite.Email,
                $"You've been asked to manage {evt.Name}",
                $"You have been invited to take on the event \"{evt.Name}\" on Geeked On.\n\n" +
                $"Accepting lets you set your organisation up and take over managing the event " +
                $"-- its attendees, tagging and settings.\n\nOpen this link to accept: {acceptUrl}");

            TempData["SuccessMessage"] =
                $"Invite emailed to {invite.Email}. In demo mode it lands in the Outbox; " +
                $"the link is {acceptUrl}";

            return RedirectToAction(nameof(Index), new { eventId });
        }

        // POST /Handover/{eventId}/{inviteId}/Revoke
        [HttpPost]
        [Route("Handover/{eventId}/{inviteId}/Revoke")]
        [ValidateAntiForgeryToken]
        [RequireEventRole(EventRole.Coordinator)]
        public async Task<IActionResult> Revoke(Guid eventId, Guid inviteId)
        {
            var invite = await _db.OrganisationHandovers
                .FirstOrDefaultAsync(h => h.Id == inviteId && h.EventId == eventId);

            if (invite is null)
            {
                return NotFound();
            }

            if (invite.AcceptedAt is not null)
            {
                TempData["ErrorMessage"] = "That invite was already accepted.";
                return RedirectToAction(nameof(Index), new { eventId });
            }

            invite.Revoked = true;
            await _db.SaveChangesAsync();

            TempData["SuccessMessage"] = "Invite withdrawn.";
            return RedirectToAction(nameof(Index), new { eventId });
        }

        // GET /Handover/Accept/{token} -- the invited person names their organisation.
        [HttpGet]
        [Route("Handover/Accept/{token}")]
        public async Task<IActionResult> Accept(string token)
        {
            var invite = await FindOpenInviteAsync(token);
            if (invite is null)
            {
                return View("HandoverInvalid");
            }

            ViewData["Token"] = token;
            ViewData["EventName"] = invite.Event?.Name;
            ViewData["SuggestedEmail"] = invite.Email;

            return View();
        }

        // POST /Handover/Accept/{token} -- creates the organisation, links the event, and
        // makes the accepter its Coordinator.
        [HttpPost]
        [Route("Handover/Accept/{token}")]
        [ValidateAntiForgeryToken]
        [ActionName("Accept")]
        public async Task<IActionResult> AcceptConfirmed(string token, string organisationName, string contactEmail)
        {
            var invite = await FindOpenInviteAsync(token);
            if (invite is null)
            {
                return View("HandoverInvalid");
            }

            var userIdText = _userManager.GetUserId(User);
            if (userIdText is null || !Guid.TryParse(userIdText, out var userId))
            {
                return Challenge();
            }

            if (string.IsNullOrWhiteSpace(organisationName))
            {
                TempData["ErrorMessage"] = "Give your organisation a name.";
                return RedirectToAction(nameof(Accept), new { token });
            }

            var evt = invite.Event!;
            if (evt.OrganisationId is not null)
            {
                return View("HandoverInvalid");
            }

            var organisation = new Organisation
            {
                Id = Guid.NewGuid(),
                Name = organisationName.Trim(),
                ContactEmail = string.IsNullOrWhiteSpace(contactEmail) ? invite.Email : contactEmail.Trim(),
                AdminUserId = userId
            };
            _db.Organisations.Add(organisation);

            evt.OrganisationId = organisation.Id;

            // Access is EventMembership, so taking the event on has to grant one. The
            // photographer's own memberships are left alone -- they shot it and still need
            // their gallery.
            var alreadyCoordinator = await _db.EventMemberships.AnyAsync(m =>
                m.EventId == evt.Id && m.UserId == userId && m.Role == EventRole.Coordinator);

            if (!alreadyCoordinator)
            {
                _db.EventMemberships.Add(new EventMembership
                {
                    Id = Guid.NewGuid(),
                    EventId = evt.Id,
                    UserId = userId,
                    Role = EventRole.Coordinator
                });
            }

            invite.AcceptedAt = DateTime.UtcNow;
            invite.CreatedOrganisationId = organisation.Id;

            await _db.SaveChangesAsync();

            // Same reasoning as ClaimController and DelegatesController: the dashboard reads
            // the sticky ActiveRole claim, not a live membership query, and Coordinator is one
            // of the two values that claim never lets go of on its own (ResolveActiveRoleAsync
            // keeps it "remembered" even once it stops matching anything held) -- so someone
            // who registered as a Photographer and accepts a handover would be stuck on the
            // photographer dashboard even after signing out and back in, unless this runs.
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser is not null)
            {
                var existingClaim = (await _userManager.GetClaimsAsync(currentUser))
                    .FirstOrDefault(c => c.Type == "ActiveRole");
                var newClaim = new System.Security.Claims.Claim("ActiveRole", EventRole.Coordinator.ToString());
                if (existingClaim is null)
                {
                    await _userManager.AddClaimAsync(currentUser, newClaim);
                    await _signInManager.RefreshSignInAsync(currentUser);
                }
                else if (existingClaim.Value != newClaim.Value)
                {
                    await _userManager.ReplaceClaimAsync(currentUser, existingClaim, newClaim);
                    await _signInManager.RefreshSignInAsync(currentUser);
                }
            }

            TempData["SuccessMessage"] = $"{organisation.Name} now manages {evt.Name}.";

            return RedirectToAction("Index", "Events");
        }

        private async Task<OrganisationHandover?> FindOpenInviteAsync(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            var invite = await _db.OrganisationHandovers
                .Include(h => h.Event)
                .FirstOrDefaultAsync(h => h.Token == token);

            if (invite is null || invite.Revoked || invite.AcceptedAt is not null || invite.Event is null)
            {
                return null;
            }

            return invite;
        }
    }
}
