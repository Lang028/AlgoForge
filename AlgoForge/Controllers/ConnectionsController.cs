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
    // D10: connections are global links between two claimed users; MetAtEventId only records
    // where they met. D11: contact details are shared when -- and only when -- the request is
    // accepted (or the attendee has opted contacts visible).
    //
    // Nothing here touches photo visibility. Photos stay up for every event member whatever
    // the connection state is; a connection shares *contact details*, not access to pictures.
    [Authorize]
    public class ConnectionsController : Controller
    {
        private readonly AlgoForgeDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IEmailSender _emailSender;

        public ConnectionsController(
            AlgoForgeDbContext db,
            UserManager<ApplicationUser> userManager,
            IEmailSender emailSender)
        {
            _db = db;
            _userManager = userManager;
            _emailSender = emailSender;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var viewerId = CurrentUserId();
            if (viewerId is null)
            {
                return Challenge();
            }

            var connections = await _db.Connections
                .Where(c => c.RequesterId == viewerId || c.ReceiverId == viewerId)
                .Include(c => c.Requester)
                .Include(c => c.Receiver)
                .Include(c => c.MetAtEvent)
                .OrderByDescending(c => c.CreatedAt)
                .ToListAsync();

            // Contact details come off the event-scoped Attendee record, so resolve the ones
            // the viewer is allowed to see rather than exposing every attendee row.
            var otherUserIds = connections.Select(c => c.OtherUserId(viewerId.Value)).Distinct().ToList();
            var attendees = await _db.Attendees
                .Where(a => a.ClaimedByUserId != null && otherUserIds.Contains(a.ClaimedByUserId!.Value))
                .ToListAsync();

            ViewData["ViewerId"] = viewerId.Value;
            ViewData["Attendees"] = attendees;
            return View(connections);
        }

        // POST /Connections/Request -- sent from a face in the gallery. Named Send() because
        // a Request() action would hide ControllerBase.Request, which this method needs.
        //
        // Delegates are excluded on purpose: D5 gives them view and download and no write
        // actions at all, and reaching out to someone is very much a write. The confirmed-tag
        // check below guards who you may reach; this guards whether you had any business in
        // the event to begin with, which it previously did not check.
        [HttpPost]
        [ActionName("Request")]
        [ValidateAntiForgeryToken]
        [RequireEventRole(EventRole.Coordinator, EventRole.Photographer, EventRole.Attendee)]
        public async Task<IActionResult> Send(Guid eventId, Guid attendeeId)
        {
            var viewerId = CurrentUserId();
            if (viewerId is null)
            {
                return Challenge();
            }

            var attendee = await _db.Attendees
                .FirstOrDefaultAsync(a => a.Id == attendeeId && a.EventId == eventId);

            if (attendee is null)
            {
                return NotFound();
            }

            if (attendee.ClaimedByUserId is null)
            {
                TempData["ErrorMessage"] = $"{attendee.Name} hasn't claimed their invite yet, so there's no one to accept.";
                return BackToGallery(eventId);
            }

            var targetUserId = attendee.ClaimedByUserId.Value;
            if (targetUserId == viewerId)
            {
                TempData["ErrorMessage"] = "That's you.";
                return BackToGallery(eventId);
            }

            // You can only reach out to someone the gallery was allowed to name to you --
            // i.e. someone who confirmed a tag here. Without this, a guessed attendee id
            // would let you request a connection with a person who never consented to being
            // identified at this event.
            var isConfirmedHere = await _db.Tags.AnyAsync(t =>
                t.TaggedAttendeeId == attendee.Id && t.Status == TagStatus.Confirmed);

            if (!isConfirmedHere)
            {
                return Forbid();
            }

            var (low, high) = Order(viewerId.Value, targetUserId);
            var existing = await _db.Connections
                .FirstOrDefaultAsync(c => c.PairLowId == low && c.PairHighId == high);

            if (existing is not null)
            {
                TempData["ErrorMessage"] = existing.Status switch
                {
                    ConnectionStatus.Accepted => $"You're already connected with {attendee.Name}.",
                    ConnectionStatus.Declined => "That request was already answered.",
                    _ => "There's already a pending request between you two."
                };
                return BackToGallery(eventId);
            }

            var connection = Connection.Create(viewerId.Value, targetUserId, eventId);
            _db.Connections.Add(connection);
            await _db.SaveChangesAsync();

            var requesterName = await DisplayNameAsync(viewerId.Value);
            var respondUrl = Url.Action(nameof(Respond), "Connections",
                new { token = connection.ResponseToken }, Request.Scheme)!;

            await _emailSender.SendEmailAsync(attendee.Email,
                $"{requesterName} wants to connect",
                $"You were both at {(await _db.Events.FindAsync(eventId))?.Name}. " +
                $"Accept or decline here: {respondUrl}");

            TempData["SuccessMessage"] =
                $"Request sent to {attendee.Name}. They'll see your details once they accept. " +
                $"Respond link (dev mode, not actually emailed): {respondUrl}";

            return BackToGallery(eventId);
        }

        // GET /Connections/Respond/{token} -- landing page for the emailed link.
        // Deliberately a confirmation page rather than a GET that mutates: an email client
        // prefetching links shouldn't be able to accept a connection on your behalf.
        [HttpGet]
        [Route("Connections/Respond/{token}")]
        public async Task<IActionResult> Respond(string token)
        {
            var viewerId = CurrentUserId();
            if (viewerId is null)
            {
                return Challenge();
            }

            var connection = await _db.Connections
                .Include(c => c.Requester)
                .Include(c => c.MetAtEvent)
                .FirstOrDefaultAsync(c => c.ResponseToken == token);

            if (connection is null)
            {
                return NotFound("This link isn't valid.");
            }

            if (connection.ReceiverId != viewerId)
            {
                return Forbid();
            }

            return View(connection);
        }

        [HttpPost]
        [Route("Connections/{connectionId}/Accept")]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> Accept(Guid connectionId, string? returnEventId) =>
            RespondTo(connectionId, ConnectionStatus.Accepted, returnEventId);

        [HttpPost]
        [Route("Connections/{connectionId}/Decline")]
        [ValidateAntiForgeryToken]
        public Task<IActionResult> Decline(Guid connectionId, string? returnEventId) =>
            RespondTo(connectionId, ConnectionStatus.Declined, returnEventId);

        private async Task<IActionResult> RespondTo(Guid connectionId, ConnectionStatus status, string? returnEventId)
        {
            var viewerId = CurrentUserId();
            if (viewerId is null)
            {
                return Challenge();
            }

            var connection = await _db.Connections.FirstOrDefaultAsync(c => c.Id == connectionId);

            // Only the person who was asked can answer.
            if (connection is null || connection.ReceiverId != viewerId)
            {
                return Forbid();
            }

            if (connection.Status != ConnectionStatus.Pending)
            {
                TempData["ErrorMessage"] = "That request was already answered.";
                return RedirectToAction(nameof(Index));
            }

            connection.Status = status;
            connection.RespondedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            if (status == ConnectionStatus.Accepted)
            {
                await SendContactExchangeAsync(connection);
                TempData["SuccessMessage"] = "Connected. You've both been emailed each other's details.";
            }
            else
            {
                TempData["SuccessMessage"] = "Request declined. Your details weren't shared.";
            }

            if (Guid.TryParse(returnEventId, out var eventId))
            {
                return BackToGallery(eventId);
            }

            return RedirectToAction(nameof(Index));
        }

        // On acceptance both sides get the other's details -- a connection is mutual, so a
        // one-way email would leave the person who accepted with nothing.
        private async Task SendContactExchangeAsync(Connection connection)
        {
            var requester = await ContactCardAsync(connection.RequesterId, connection.MetAtEventId);
            var receiver = await ContactCardAsync(connection.ReceiverId, connection.MetAtEventId);

            if (requester is null || receiver is null)
            {
                return;
            }

            var eventName = connection.MetAtEventId is null
                ? "the event"
                : (await _db.Events.FindAsync(connection.MetAtEventId))?.Name ?? "the event";

            await _emailSender.SendEmailAsync(requester.Email,
                $"{receiver.Name} accepted your connection request",
                $"You met at {eventName}.\n\n{receiver.Describe()}");

            await _emailSender.SendEmailAsync(receiver.Email,
                $"You're connected with {requester.Name}",
                $"You met at {eventName}.\n\n{requester.Describe()}");
        }

        private async Task<ContactCard?> ContactCardAsync(Guid userId, Guid? eventId)
        {
            var attendee = eventId is null
                ? null
                : await _db.Attendees.FirstOrDefaultAsync(a => a.EventId == eventId && a.ClaimedByUserId == userId);

            if (attendee is not null)
            {
                return new ContactCard(attendee.Name, attendee.Email, attendee.ContactInfo);
            }

            // Photographers and coordinators have no Attendee row -- fall back to the account.
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
            return user is null ? null : new ContactCard(user.DisplayName, user.Email ?? string.Empty, string.Empty);
        }

        private sealed record ContactCard(string Name, string Email, string ContactInfo)
        {
            public string Describe() => string.IsNullOrWhiteSpace(ContactInfo)
                ? $"{Name}\n{Email}"
                : $"{Name}\n{Email}\n{ContactInfo}";
        }

        private async Task<string> DisplayNameAsync(Guid userId)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
            return string.IsNullOrWhiteSpace(user?.DisplayName) ? "Someone" : user!.DisplayName;
        }

        private IActionResult BackToGallery(Guid eventId) =>
            RedirectToAction("Index", "Photos", new { eventId });

        private static (Guid Low, Guid High) Order(Guid a, Guid b) =>
            a.CompareTo(b) <= 0 ? (a, b) : (b, a);

        private Guid? CurrentUserId()
        {
            var userIdText = _userManager.GetUserId(User);
            return userIdText is not null && Guid.TryParse(userIdText, out var userId) ? userId : null;
        }
    }
}
