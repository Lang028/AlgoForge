using AlgoForge.Models;

namespace AlgoForge.ViewModels.Events
{
    /// <summary>One card on the Events index grid.</summary>
    public class EventCardViewModel
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string OrganisationName { get; set; } = string.Empty;
        public DateTime EventDate { get; set; }
        public EventStatus Status { get; set; }

        /// <summary>First visible photo of the event, used as the card image; null shows a gradient placeholder.</summary>
        public string? CoverUrl { get; set; }

        public int PhotoCount { get; set; }
        public int AttendeeCount { get; set; }
    }
}
