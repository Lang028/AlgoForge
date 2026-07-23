namespace AlgoForge.Models
{
    public class Comment
    {
        public int Id { get; set; }

        public int PhotoId { get; set; }
        public Photo Photo { get; set; } = null!;

        public string AuthorUserId { get; set; } = string.Empty;

        public string Body { get; set; } = string.Empty;

        public DateTime PostedAt { get; set; } = DateTime.UtcNow;

        public bool IsDeleted { get; set; } = false;
        public string? ModeratedByUserId { get; set; }
    }
}
