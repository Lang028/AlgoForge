namespace AlgoForge.ViewModels.Share
{
    public class SharedGalleryViewModel
    {
        public Guid EventId { get; set; }
        public string EventName { get; set; } = string.Empty;
        public DateTime EventDate { get; set; }

        // Ids only. The bytes still come from PhotosController.File, which re-checks the
        // cookie on every request rather than trusting that this page was rendered.
        public List<Guid> PhotoIds { get; set; } = new();
    }
}
