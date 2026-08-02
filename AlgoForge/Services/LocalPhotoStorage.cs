using AlgoForge.Models;

namespace AlgoForge.Services
{
    // Development storage: the original App_Data/uploads behaviour, unchanged.
    //
    // App_Data and not wwwroot, because wwwroot is watched by dotnet watch / Visual Studio
    // hot reload -- writing uploads there makes every upload look like a source change and
    // the dev server refreshes the browser mid-upload, which is what made the batched
    // uploader unusable for the photographer.
    public class LocalPhotoStorage : IPhotoStorage
    {
        private readonly IWebHostEnvironment _env;

        public LocalPhotoStorage(IWebHostEnvironment env)
        {
            _env = env;
        }

        private string Root => Path.Combine(_env.ContentRootPath, "App_Data", "uploads");

        public async Task<string> SaveAsync(
            Guid eventId,
            string fileName,
            Stream content,
            CancellationToken cancellationToken)
        {
            var directory = Path.Combine(Root, eventId.ToString());
            Directory.CreateDirectory(directory);

            var filePath = Path.Combine(directory, fileName);

            await using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await content.CopyToAsync(stream, cancellationToken);
            }

            return $"/uploads/{eventId}/{fileName}";
        }

        public Task<Stream?> OpenReadAsync(Photo photo, CancellationToken cancellationToken)
        {
            var relative = PhotoStorageKey.FromBlobUrl(photo.BlobUrl);

            var full = Path.GetFullPath(
                Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar)));

            // Never let a stored value escape the uploads root. The database is not a
            // trusted source of paths: a BlobUrl of "../../appsettings.json" would
            // otherwise be served to anyone with access to the event.
            if (!full.StartsWith(Path.GetFullPath(Root), StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult<Stream?>(null);
            }

            if (!File.Exists(full))
            {
                return Task.FromResult<Stream?>(null);
            }

            Stream stream = new FileStream(full, FileMode.Open, FileAccess.Read);
            return Task.FromResult<Stream?>(stream);
        }
    }
}
