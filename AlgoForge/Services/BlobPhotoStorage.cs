using AlgoForge.Models;
using Azure;
using Azure.Storage.Blobs;

namespace AlgoForge.Services
{
    // Production storage: Azure Blob Storage.
    //
    // The container is private. Nothing here hands out a public or SAS URL, deliberately --
    // every image in the app is already served through PhotosController.File, which checks
    // event membership on each request. A public blob URL would reintroduce exactly the
    // hole that endpoint was written to close: a link that works for anyone holding it, with
    // no account, and that keeps working after the holder is removed from the event.
    //
    // Blobs are keyed "{eventId}/{fileName}", which is the same string Photo.BlobUrl already
    // stores after its "/uploads/" prefix. Nothing about an existing row needs to change.
    public class BlobPhotoStorage : IPhotoStorage
    {
        private readonly BlobContainerClient _container;
        private readonly ILogger<BlobPhotoStorage> _logger;

        public BlobPhotoStorage(IConfiguration configuration, ILogger<BlobPhotoStorage> logger)
        {
            _logger = logger;

            var connectionString = configuration["Storage:ConnectionString"]
                ?? throw new InvalidOperationException(
                    "Storage:ConnectionString is missing. Production photo storage cannot start without it.");

            var containerName = configuration["Storage:Container"] ?? "photos";

            _container = new BlobContainerClient(connectionString, containerName);
        }

        public async Task<string> SaveAsync(
            Guid eventId,
            string fileName,
            Stream content,
            CancellationToken cancellationToken)
        {
            var key = $"{eventId}/{fileName}";

            var blob = _container.GetBlobClient(key);
            await blob.UploadAsync(content, overwrite: true, cancellationToken);

            return $"/uploads/{key}";
        }

        public async Task<Stream?> OpenReadAsync(Photo photo, CancellationToken cancellationToken)
        {
            var key = PhotoStorageKey.FromBlobUrl(photo.BlobUrl);

            // A stored key is not a filesystem path here, but a traversal segment would
            // still be a sign the value has been tampered with rather than written by
            // SaveAsync -- refuse it instead of asking the service about it.
            if (key.Contains(".."))
            {
                return null;
            }

            try
            {
                var blob = _container.GetBlobClient(key);
                return await blob.OpenReadAsync(cancellationToken: cancellationToken);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // The row survived but the blob did not. Same contract as the local
                // implementation: null means "nothing to read", not "retry".
                _logger.LogWarning(
                    "Photo {PhotoId} has no blob at {Key}.", photo.Id, key);

                return null;
            }
        }
    }
}
