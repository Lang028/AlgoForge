using AlgoForge.Models;

namespace AlgoForge.Services
{
    // Where a photo's bytes actually live. Two implementations, chosen in Program.cs the
    // same way IEmailSender is: local disk for development, Azure Blob in production.
    //
    // The split exists because App Service gives a container a filesystem that is wiped on
    // every restart, redeploy and scale event. Writing uploads there means the Photo rows
    // survive in SQL while the files they point at silently disappear -- a gallery of
    // broken images with no way back. Development keeps writing to App_Data so the local
    // workflow needs no Azure account and no connection string.
    //
    // Photo.BlobUrl keeps its original "/uploads/{eventId}/{fileName}" shape in both, so
    // moving between them needs no migration and no rewrite of existing rows: the local
    // implementation reads it as a path under App_Data/uploads, the blob one as the key
    // "{eventId}/{fileName}" inside the container.
    public interface IPhotoStorage
    {
        // Stores the bytes and returns the value to persist as Photo.BlobUrl.
        Task<string> SaveAsync(
            Guid eventId,
            string fileName,
            Stream content,
            CancellationToken cancellationToken);

        // Opens the stored bytes, or null when the row outlived its file. Callers are
        // expected to treat null as "nothing to read" rather than an error -- see
        // PhotoDetectionWorker, which marks the photo Failed rather than retrying forever.
        Task<Stream?> OpenReadAsync(Photo photo, CancellationToken cancellationToken);
    }

    public static class PhotoStorageKey
    {
        // "/uploads/{eventId}/{fileName}" -> "{eventId}/{fileName}". Tolerates a value
        // stored without the prefix, which is what the blob implementation writes if the
        // format is ever changed underneath it.
        public static string FromBlobUrl(string blobUrl) =>
            blobUrl.StartsWith("/uploads/")
                ? blobUrl["/uploads/".Length..]
                : blobUrl.TrimStart('/');
    }
}
