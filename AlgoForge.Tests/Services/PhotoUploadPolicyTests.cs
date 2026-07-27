using AlgoForge.Services;
using Microsoft.AspNetCore.Http;

namespace AlgoForge.Tests.Services
{
    // The policy decides what gets written under wwwroot and served back as a static file,
    // so "HTML that calls itself a JPEG is refused" is a security boundary rather than a
    // nicety -- accepting one would put an attacker-authored page on the app's own origin.
    // These tests pin that, plus the folder-upload behaviour that depends on it: a folder
    // full of camera-card junk must skip the junk rather than fail the batch.
    public class PhotoUploadPolicyTests
    {
        private static IFormFile FileWith(byte[] content, string fileName, string contentType = "image/jpeg")
        {
            var stream = new MemoryStream(content);
            return new FormFile(stream, 0, content.Length, "files", fileName)
            {
                Headers = new HeaderDictionary(),
                ContentType = contentType
            };
        }

        // Real headers, padded out past the 12 bytes the sniffer reads.
        private static byte[] Jpeg() => Padded(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 });
        private static byte[] Png() => Padded(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        private static byte[] Bmp() => Padded(new byte[] { 0x42, 0x4D });

        private static byte[] WebP()
        {
            var bytes = new byte[64];
            "RIFF"u8.ToArray().CopyTo(bytes, 0);
            "WEBP"u8.ToArray().CopyTo(bytes, 8);
            return bytes;
        }

        private static byte[] Padded(byte[] magic)
        {
            var bytes = new byte[64];
            magic.CopyTo(bytes, 0);
            return bytes;
        }

        [Theory]
        [InlineData("jpeg", ".jpg")]
        [InlineData("png", ".png")]
        [InlineData("webp", ".webp")]
        [InlineData("bmp", ".bmp")]
        public async Task Accepts_supported_formats_and_names_them_from_their_bytes(string format, string expected)
        {
            var content = format switch
            {
                "jpeg" => Jpeg(),
                "png" => Png(),
                "webp" => WebP(),
                _ => Bmp()
            };

            // Deliberately a misleading filename: the stored extension must come from the
            // content, so a PNG uploaded as "photo.jpg" is still stored as .png.
            var result = await PhotoUploadPolicy.InspectAsync(FileWith(content, "photo.jpg"));

            Assert.True(result.Accepted);
            Assert.Equal(expected, result.Extension);
        }

        // The one that matters: extension and MIME type both claim an image, the bytes do
        // not. Trusting either would land executable HTML in wwwroot on the app's origin.
        [Fact]
        public async Task Rejects_html_disguised_as_a_jpeg()
        {
            var html = System.Text.Encoding.UTF8.GetBytes("<script>alert(document.cookie)</script>");

            var result = await PhotoUploadPolicy.InspectAsync(FileWith(html, "evil.jpg", "image/jpeg"));

            Assert.False(result.Accepted);
            Assert.Null(result.Extension);
        }

        // A folder off a camera card carries these next to the photographs. Each must be
        // skipped with a reason; the controller turns that into "N files skipped" rather
        // than failing the whole upload.
        [Theory]
        [InlineData("Thumbs.db")]
        [InlineData(".DS_Store")]
        [InlineData("IMG_2201.xmp")]
        public async Task Rejects_the_sidecar_files_a_folder_upload_carries(string fileName)
        {
            var junk = System.Text.Encoding.UTF8.GetBytes("not an image at all");

            var result = await PhotoUploadPolicy.InspectAsync(FileWith(junk, fileName, "application/octet-stream"));

            Assert.False(result.Accepted);
            Assert.NotNull(result.Reason);
        }

        [Fact]
        public async Task Rejects_an_empty_file()
        {
            var result = await PhotoUploadPolicy.InspectAsync(FileWith(Array.Empty<byte>(), "empty.jpg"));

            Assert.False(result.Accepted);
            Assert.Equal("empty file", result.Reason);
        }

        [Fact]
        public async Task Rejects_a_file_over_the_per_file_cap()
        {
            // Streaming a real 25MB+ buffer would make this test needlessly slow, so the
            // length is overstated against a small body -- the size check reads Length only.
            var content = Jpeg();
            var stream = new MemoryStream(content);
            var file = new FormFile(stream, 0, PhotoUploadPolicy.MaxFileBytes + 1, "files", "huge.jpg")
            {
                Headers = new HeaderDictionary(),
                ContentType = "image/jpeg"
            };

            var result = await PhotoUploadPolicy.InspectAsync(file);

            Assert.False(result.Accepted);
            Assert.Contains("larger than", result.Reason);
        }

        // A truncated file must not be mistaken for a valid one: the sniffer reads up to 12
        // bytes and has to cope with a file shorter than that.
        [Fact]
        public async Task Rejects_a_file_shorter_than_the_signature()
        {
            var result = await PhotoUploadPolicy.InspectAsync(FileWith(new byte[] { 0xFF, 0xD8 }, "truncated.jpg"));

            Assert.False(result.Accepted);
        }

        // The old 50MB ceiling was roughly ten real photos, and going over it failed as an
        // unexplained 400. Folder upload depends on the headroom, so the constant is pinned.
        [Fact]
        public async Task Request_cap_leaves_room_for_a_realistic_batch()
        {
            Assert.True(PhotoUploadPolicy.MaxRequestBytes >= 100L * 1024 * 1024);
            await Task.CompletedTask;
        }
    }
}
