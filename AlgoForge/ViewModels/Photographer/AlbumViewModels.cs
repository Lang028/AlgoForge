using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace AlgoForge.ViewModels.Photographer
{
    public class AlbumListViewModel
    {
        public int EventId { get; set; }
        public string EventName { get; set; } = string.Empty;
        public bool UploadWindowOpen { get; set; }
        public List<AlbumSummary> Albums { get; set; } = new();
    }

    public class AlbumSummary
    {
        public int AlbumId { get; set; }
        public string Name { get; set; } = string.Empty;
        public int PhotoCount { get; set; }
        public string? CoverThumbnailUrl { get; set; }
    }

    public class CreateAlbumViewModel
    {
        public int EventId { get; set; }

        [Required, MaxLength(150)]
        public string Name { get; set; } = string.Empty;
    }

    public class UploadPhotosViewModel
    {
        public int AlbumId { get; set; }
        public string AlbumName { get; set; } = string.Empty;
        public int EventId { get; set; }

        [Required]
        [Display(Name = "Photos")]
        public List<IFormFile> Files { get; set; } = new();
    }
}
