using AlgoForge.Data;
using AlgoForge.Models;
using AlgoForge.ViewModels.Share;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AlgoForge.Controllers
{
    // The plain gallery: a link, the photographs behind it, and nothing else. No account,
    // no invitation, no tagging, no connection requests -- and no face detection, which is
    // refused at upload for these events rather than merely hidden here.
    //
    // The token is exchanged for a cookie on the way in and never appears again. Putting it
    // in each image URL instead would write it into browser history, hand it to every site
    // linked from the page through the referrer header, and leave it sitting in any proxy
    // log between here and the viewer -- for a secret whose whole job is to be the one thing
    // guarding the gallery.
    [AllowAnonymous]
    public class ShareController : Controller
    {
        private readonly AlgoForgeDbContext _db;

        public ShareController(AlgoForgeDbContext db)
        {
            _db = db;
        }

        // Cookie name is per event, so holding a link to one gallery grants nothing anywhere
        // else, and someone given two links keeps both.
        public static string CookieName(Guid eventId) => $"gallery_{eventId:N}";

        [HttpGet]
        [Route("g/{token}")]
        public async Task<IActionResult> Gallery(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return View("LinkInvalid");
            }

            var evt = await _db.Events
                .FirstOrDefaultAsync(e =>
                    e.GalleryMode == EventGalleryMode.LinkShared && e.ShareToken == token);

            if (evt is null)
            {
                // One answer for a token that never existed and one that has been rotated:
                // telling them apart would let anyone confirm which links were once real.
                return View("LinkInvalid");
            }

            Response.Cookies.Append(CookieName(evt.Id), token, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Lax,
                IsEssential = true,

                // Long enough to browse and download in one sitting; short enough that a
                // borrowed browser does not stay admitted indefinitely. The link itself is
                // what grants access, and it can simply be opened again.
                MaxAge = TimeSpan.FromHours(12)
            });

            var photos = await _db.Photos
                .Where(p => p.EventId == evt.Id && p.Status == PhotoStatus.Visible)
                .OrderByDescending(p => p.UploadedAt)
                .Select(p => p.Id)
                .ToListAsync();

            return View(new SharedGalleryViewModel
            {
                EventId = evt.Id,
                EventName = evt.Name,
                EventDate = evt.EventDate,
                PhotoIds = photos
            });
        }
    }
}
