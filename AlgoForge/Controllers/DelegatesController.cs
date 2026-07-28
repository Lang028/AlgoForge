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
    // D5: an attendee may nominate a few people to view the gallery on their behalf.
    //
    // The nomination belongs to the attendee, not the coordinator: it is their likeness in
    // the photographs, so it is their call who else may look. Everything here is therefore
    // gated on being the attendee in question, never on running the event.
    [Authorize]
    public class DelegatesController : Controller
    {
        // D5 caps delegates per attendee. Three covers "my partner, my mum, my friend"
        // without turning a personal courtesy into a second guest list.
        public const int MaxPerAttendee = 3;

        private readonly AlgoForgeDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IEmailSender _emailSender;

        public DelegatesController(
            AlgoForgeDbContext db,
            UserManager<ApplicationUser> userManager,
            IEmailSender emailSender)
        {
            _db = db;
            _userManager = userManager;
            _emailSender = emailSender;
        }

        // GET /Delegates/{eventId} -- the attendee's own list for one event.
        [HttpGet]
        [Route("Delegates/{eventId}")]
        [RequireEventRole(EventRole.Attendee)]
        public async Task<IActionResult> Index(Guid eventId)
        {
            var attendee = await CurrentAttendeeAsync(eventId);
            if (attendee is null)
            {
                return Forbid();
            }

            ViewData["EventId"] = eventId;
            ViewData["EventName"] = (await _db.Events.FindAsync(eventId))?.Name ?? "Event";
            ViewData["MaxPerAttendee"] = MaxPerAttendee;

            var invites = await _db.DelegateInvites
                .Where(d => d.AttendeeId == attendee.Id && !d.Revoked)
                .OrderBy(d => d.CreatedAt)
                .ToListAsync();

            return View(invites);
        }

        // POST /Delegates/{eventId}/Invite
        [HttpPost]
        [Route("Delegates/{eventId}/Invite")]
        [ValidateAntiForgeryToken]
        [RequireEventRole(EventRole.Attendee)]
        public async Task<IActionResult> Invite(Guid eventId, string name, string email)
        {
            var attendee = await CurrentAttendeeAsync(eventId);
            if (attendee is null)
            {
                return Forbid();
            }

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email))
            {
                TempData["ErrorMessage"] = "A delegate needs both a name and an email address.";
                return RedirectToAction(nameof(Index), new { eventId });
            }

            var active = await _db.DelegateInvites
                .CountAsync(d => d.AttendeeId == attendee.Id && !d.Revoked);

            if (active >= MaxPerAttendee)
            {
                TempData["ErrorMessage"] =
                    $"You can have at most {MaxPerAttendee} delegates. Revoke one to add another.";
                return RedirectToAction(nameof(Index), new { eventId });
            }

            var trimmedEmail = email.Trim();
            var duplicate = await _db.DelegateInvites.AnyAsync(d =>
                d.AttendeeId == attendee.Id && !d.Revoked && d.Email == trimmedEmail);

            if (duplicate)
            {
                TempData["ErrorMessage"] = $"{trimmedEmail} is already one of your delegates.";
                return RedirectToAction(nameof(Index), new { eventId });
            }

            var invite = new DelegateInvite
            {
                Id = Guid.NewGuid(),
                EventId = eventId,
                AttendeeId = attendee.Id,
                GrantedByUserId = attendee.ClaimedByUserId!.Value,
                Name = name.Trim(),
                Email = trimmedEmail,
                Token = Guid.NewGuid().ToString("N")
            };

            _db.DelegateInvites.Add(invite);
            await _db.SaveChangesAsync();

            var claimUrl = Url.Action(nameof(Claim), "Delegates",
                new { token = invite.Token }, Request.Scheme)!;

            var eventName = (await _db.Events.FindAsync(eventId))?.Name ?? "an event";

            await _emailSender.SendEmailAsync(invite.Email,
                $"{attendee.Name} shared their event photos with you",
                $"{attendee.Name} has asked that you be able to view the photographs from {eventName} " +
                $"on their behalf.\n\nOpen this link to get access: {claimUrl}\n\n" +
                $"You will be able to view and download the gallery. You cannot tag people or " +
                $"answer connection requests -- those stay with {attendee.Name}.");

            TempData["SuccessMessage"] =
                $"Invite emailed to {invite.Name}. In demo mode it lands in the Outbox; " +
                $"the link is {claimUrl}";

            return RedirectToAction(nameof(Index), new { eventId });
        }

        // POST /Delegates/{eventId}/{inviteId}/Revoke -- withdraws the nomination and the
        // membership it created, so access stops immediately.
        [HttpPost]
        [Route("Delegates/{eventId}/{inviteId}/Revoke")]
        [ValidateAntiForgeryToken]
        [RequireEventRole(EventRole.Attendee)]
        public async Task<IActionResult> Revoke(Guid eventId, Guid inviteId)
        {
            var attendee = await CurrentAttendeeAsync(eventId);
            if (attendee is null)
            {
                return Forbid();
            }

            var invite = await _db.DelegateInvites
                .FirstOrDefaultAsync(d => d.Id == inviteId && d.AttendeeId == attendee.Id);

            if (invite is null)
            {
                return NotFound();
            }

            invite.Revoked = true;

            if (invite.ClaimedByUserId is not null)
            {
                var membership = await _db.EventMemberships.FirstOrDefaultAsync(m =>
                    m.EventId == invite.EventId
                    && m.UserId == invite.ClaimedByUserId
                    && m.Role == EventRole.Delegate
                    && m.GrantedByUserId == invite.GrantedByUserId);

                if (membership is not null)
                {
                    _db.EventMemberships.Remove(membership);
                }
            }

            await _db.SaveChangesAsync();

            TempData["SuccessMessage"] = $"{invite.Name} no longer has access.";
            return RedirectToAction(nameof(Index), new { eventId });
        }

        // GET /Delegates/Claim/{token} -- the delegate's side of the emailed link.
        // Sign-in first, same as the attendee claim flow.
        [HttpGet]
        [Route("Delegates/Claim/{token}")]
        public async Task<IActionResult> Claim(string token)
        {
            var invite = await _db.DelegateInvites
                .Include(d => d.Attendee)
                .FirstOrDefaultAsync(d => d.Token == token);

            if (invite is null || invite.Revoked)
            {
                return View("InviteInvalid");
            }

            var userIdText = _userManager.GetUserId(User);
            if (userIdText is null || !Guid.TryParse(userIdText, out var userId))
            {
                return Challenge();
            }

            // A link is for one person. Once claimed it belongs to that account.
            if (invite.ClaimedByUserId is not null && invite.ClaimedByUserId != userId)
            {
                return View("InviteInvalid");
            }

            invite.ClaimedByUserId ??= userId;

            var alreadyMember = await _db.EventMemberships.AnyAsync(m =>
                m.EventId == invite.EventId && m.UserId == userId && m.Role == EventRole.Delegate);

            if (!alreadyMember)
            {
                _db.EventMemberships.Add(new EventMembership
                {
                    Id = Guid.NewGuid(),
                    EventId = invite.EventId,
                    UserId = userId,
                    Role = EventRole.Delegate,
                    GrantedByUserId = invite.GrantedByUserId
                });
            }

            await _db.SaveChangesAsync();

            TempData["SuccessMessage"] =
                $"You're viewing this gallery on behalf of {invite.Attendee?.Name}.";

            return RedirectToAction("Index", "Photos", new { eventId = invite.EventId });
        }

        // The Attendee row for the signed-in user in this event. Null means they hold the
        // Attendee role but haven't claimed a record here -- nothing to delegate.
        private async Task<Attendee?> CurrentAttendeeAsync(Guid eventId)
        {
            var userIdText = _userManager.GetUserId(User);
            if (userIdText is null || !Guid.TryParse(userIdText, out var userId))
            {
                return null;
            }

            return await _db.Attendees.FirstOrDefaultAsync(
                a => a.EventId == eventId && a.ClaimedByUserId == userId);
        }
    }
}
