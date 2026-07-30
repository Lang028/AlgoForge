using AlgoForge.Models;

namespace AlgoForge.Services
{
    // Resolves a Photo's BlobUrl ("/uploads/{eventId}/{fileName}") back onto its file under
    // App_Data/uploads, with a path-traversal guard. Shared by PhotosController (gallery,
    // download, the no-JavaScript retry fallback) and PhotoDetectionWorker (background
    // detection), so the same guard applies wherever a Photo's file is opened from its
    // stored path rather than being duplicated between the two.
    public static class PhotoStorage
    {
        public static string? ResolveStoredPath(IWebHostEnvironment env, Photo photo)
        {
            var relative = photo.BlobUrl.StartsWith("/uploads/")
                ? photo.BlobUrl["/uploads/".Length..]
                : photo.BlobUrl.TrimStart('/');

            var root = Path.Combine(env.ContentRootPath, "App_Data", "uploads");
            var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));

            // Never let a stored value escape the uploads root.
            if (!full.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return File.Exists(full) ? full : null;
        }
    }
}
