using AlgoForge.Models;

namespace AlgoForge.ViewModels.Photographer
{
    public class PhotographerEventSummary
    {
        public int EventId { get; set; }
        public string EventName { get; set; } = string.Empty;
        public string OrganisationName { get; set; } = string.Empty;
        public EventStatus Status { get; set; }
        public int AlbumCount { get; set; }
        public int PhotoCount { get; set; }
        public int UnidentifiedClusterCount { get; set; }
    }

    public class PhotographerDashboardViewModel
    {
        public List<PhotographerEventSummary> Events { get; set; } = new();
    }
}
