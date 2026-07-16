using AlgoForge.Data;
using AlgoForge.Models;
using AlgoForge.Services.FaceService;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AlgoForge.Controllers
{
    [Authorize]
    public class PhotosController : Controller
    {
        private readonly AlgoForgeDbContext _db;
        private readonly FaceMatchingService _faceMatchingService;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IWebHostEnvironment _env;

        public PhotosController(
            AlgoForgeDbContext db,
            FaceMatchingService faceMatchingService,
            UserManager<ApplicationUser> userManager,
            IWebHostEnvironment env)
        {
            _db = db;
            _faceMatchingService = faceMatchingService;
            _userManager = userManager;
            _env = env;
        }

        [HttpGet]
        public async Task<IActionResult> Upload(Guid eventId)
        {
            var evt = await _db.Events.FindAsync(eventId);
            if (evt is null)
            {
                return NotFound();
            }

            ViewData["EventId"] = eventId;
            ViewData["EventName"] = evt.Name;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(50_000_000)]
        public async Task<IActionResult> Upload(Guid eventId, List<IFormFile> files)
        {
            var evt = await _db.Events.FindAsync(eventId);
            if (evt is null)
            {
                return NotFound();
            }

            var userIdText = _userManager.GetUserId(User);
            if (userIdText is null || !Guid.TryParse(userIdText, out var userId))
            {
                return Challenge();
            }

            var uploadsDir = Path.Combine(_env.WebRootPath, "uploads", eventId.ToString());
            Directory.CreateDirectory(uploadsDir);

            foreach (var file in files)
            {
                if (file.Length == 0)
                {
                    continue;
                }

                var photoId = Guid.NewGuid();
                var extension = Path.GetExtension(file.FileName);
                var fileName = $"{photoId}{extension}";
                var filePath = Path.Combine(uploadsDir, fileName);

                await using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                var photo = new Photo
                {
                    Id = photoId,
                    EventId = eventId,
                    UploadedByUserId = userId,
                    BlobUrl = $"/uploads/{eventId}/{fileName}",
                    UploadedAt = DateTime.UtcNow
                };

                // Synchronous, per D19-style local dev tradeoff: no queue/worker for v1
                // (see FaceCluster.cs notes on OPEN-3/4/5).
                await _faceMatchingService.ProcessPhotoAsync(photo, filePath);

                _db.Photos.Add(photo);

                // Save per photo, not once per batch: cluster matching queries the DB for
                // existing clusters, so photo N+1 can only join a cluster created by photo N
                // if that cluster has actually been persisted.
                await _db.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Index), new { eventId });
        }

        public async Task<IActionResult> Index(Guid eventId)
        {
            var evt = await _db.Events.FindAsync(eventId);
            if (evt is null)
            {
                return NotFound();
            }

            var photos = await _db.Photos
                .Where(p => p.EventId == eventId && p.Status == PhotoStatus.Visible)
                .Include(p => p.FaceDetections)
                .OrderByDescending(p => p.UploadedAt)
                .ToListAsync();

            var clusterIds = photos
                .SelectMany(p => p.FaceDetections)
                .Where(d => d.FaceClusterId.HasValue)
                .Select(d => d.FaceClusterId!.Value)
                .Distinct()
                .ToList();

            var clusterLabels = clusterIds
                .Select((id, index) => (id, index))
                .ToDictionary(x => x.id, x => $"Person {x.index + 1}");

            ViewData["EventId"] = eventId;
            ViewData["EventName"] = evt.Name;
            ViewData["ClusterLabels"] = clusterLabels;
            return View(photos);
        }
    }
}
