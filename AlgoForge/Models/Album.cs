using System.ComponentModel.DataAnnotations;

namespace AlgoForge.Models
{
    public class Album
    {
        public int Id { get; set; }

        public int EventId { get; set; }
        public Event Event { get; set; } = null!;

        [Required, MaxLength(150)]
        public string Name { get; set; } = string.Empty;

        public string CreatedByUserId { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<Photo> Photos { get; set; } = new List<Photo>();
    }
}
