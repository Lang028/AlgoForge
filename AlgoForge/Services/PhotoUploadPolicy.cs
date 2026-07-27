namespace AlgoForge.Services
{
    // What the upload endpoint is willing to put on disk, decided server-side on bytes.
    //
    // The view's accept="image/*" is a file-picker convenience and nothing more: it is
    // trivially bypassed, and for a folder upload it barely filters at all -- a folder off
    // a camera card carries Thumbs.db, .DS_Store, sidecar .xmp files and whatever else the
    // photographer's software wrote next to the photographs. So every decision here is made
    // again on the server, from the file's own leading bytes rather than from anything the
    // browser claimed.
    //
    // The stored extension comes from the sniffed format too, never from the uploaded
    // filename. Photos are served as static files on the app's own origin, so honouring a
    // client-supplied ".html" would put an attacker-authored page on the app's own origin --
    // a stored XSS with the session cookie in reach. Sniffing closes that off by
    // construction: an unrecognised file is never written at all, and a recognised one can
    // only ever land as .jpg/.png/.webp/.bmp.
    public static class PhotoUploadPolicy
    {
        // Comfortably above a 45MP DSLR JPEG (~15MB) without letting a single file dominate
        // a batch. Enforced per file, unlike the request-level cap, so one oversized frame is
        // skipped with a reason rather than taking the whole upload down.
        public const long MaxFileBytes = 25L * 1024 * 1024;

        // Kestrel's cap on the whole multipart body. The old 50MB ceiling worked out at
        // roughly ten real photos, and it failed badly: Kestrel aborts mid-body, so the
        // antiforgery filter is the first thing to notice and the log blames a token problem
        // that does not exist. The uploader batches files to stay well under this, and the
        // form's client-side check explains an over-limit selection before it is ever sent.
        public const long MaxRequestBytes = 512L * 1024 * 1024;

        // Formats OpenCV decodes on the Python side AND browsers render in the gallery.
        // Both halves matter: a TIFF would cluster perfectly and then show up as a broken
        // image, so it is not worth accepting.
        private static readonly (string Extension, string ContentType, byte[] Magic, int Offset)[] Signatures =
        {
            (".jpg",  "image/jpeg", new byte[] { 0xFF, 0xD8, 0xFF }, 0),
            (".png",  "image/png",  new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0),
            (".bmp",  "image/bmp",  new byte[] { 0x42, 0x4D }, 0),
        };

        // Longest signature plus the WebP check's reach (bytes 8-11), so one read covers
        // every format below.
        private const int HeaderBytes = 12;

        public sealed record Result(bool Accepted, string? Extension, string? Reason);

        // Reads just far enough to identify the format. Returns the extension the file
        // should be stored under, or the reason it is being skipped -- phrased for a
        // photographer looking at a list of skipped filenames, not for a log.
        public static async Task<Result> InspectAsync(IFormFile file, CancellationToken cancellationToken = default)
        {
            if (file.Length == 0)
            {
                return new Result(false, null, "empty file");
            }

            if (file.Length > MaxFileBytes)
            {
                return new Result(false, null, $"larger than {MaxFileBytes / (1024 * 1024)}MB");
            }

            var header = new byte[HeaderBytes];
            int read;

            await using (var stream = file.OpenReadStream())
            {
                read = await ReadAtLeastAsync(stream, header, cancellationToken);
            }

            foreach (var (extension, _, magic, offset) in Signatures)
            {
                if (StartsWith(header, read, magic, offset))
                {
                    return new Result(true, extension, null);
                }
            }

            // WebP is RIFF-framed: "RIFF" then a length then "WEBP", so it needs two
            // windows checked rather than one prefix.
            if (StartsWith(header, read, "RIFF"u8.ToArray(), 0)
                && StartsWith(header, read, "WEBP"u8.ToArray(), 8))
            {
                return new Result(true, ".webp", null);
            }

            return new Result(false, null, "not a JPEG, PNG, WebP or BMP image");
        }

        public static string ContentTypeFor(string extension) =>
            Signatures.FirstOrDefault(s => s.Extension == extension).ContentType ?? "image/webp";

        private static async Task<int> ReadAtLeastAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
        {
            var total = 0;
            while (total < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken);
                if (read == 0)
                {
                    break;
                }

                total += read;
            }

            return total;
        }

        private static bool StartsWith(byte[] header, int length, byte[] magic, int offset)
        {
            if (length < offset + magic.Length)
            {
                return false;
            }

            for (var i = 0; i < magic.Length; i++)
            {
                if (header[offset + i] != magic[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
