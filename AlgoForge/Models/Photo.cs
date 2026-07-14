namespace AlgoForge.Models
{
    public class Photo
    {
        public Guid Id { get; set; }

        public Guid EventId { get; set; }
        public Event? Event { get; set; }
        public Guid? AlbumId { get; set; }
        public Album? Album { get; set; }
        public Guid UploadedByUserId { get; set; }
        public ApplicationUser? UploadedByUser { get; set; }

        public string BlobUrl { get; set; } = string.Empty;
        public DateTime? CapturedAt { get; set; }
        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

        public PhotoStatus Status { get; set; } = PhotoStatus.Visible;
    }
}
