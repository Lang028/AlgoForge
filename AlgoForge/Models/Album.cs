using System.ComponentModel.DataAnnotations;

namespace AlgoForge.Models
{
    public class Album
    {
        public Guid AlbumId { get; set; }
        public string AlbumName { get; set; } = string.Empty;
        public string AlbumDescription { get; set; } = string.Empty;
        public DateTime CreatedDate { get; set; }= DateTime.Now;
        public Guid eventId { get; set; }
        public Event? Events { get; set; }

        public ICollection<Photo> Photos { get; set; } = new List<Photo>();

    }
}
