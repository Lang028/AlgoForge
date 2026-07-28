using AlgoForge.Models;

namespace AlgoForge.ViewModels.Events
{
    /// <summary>One card on the Events index grid.</summary>
    public class EventCardViewModel
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        /// <summary>Null when the event has no organisation and its creator runs it alone.</summary>
        public string? OrganisationName { get; set; }
        public DateTime EventDate { get; set; }
        public EventStatus Status { get; set; }

        /// <summary>First visible photo of the event; null shows a gradient placeholder.</summary>
        public Guid? CoverPhotoId { get; set; }

        /// <summary>Link to the authorising Photos/File action for <see cref="CoverPhotoId"/>.</summary>
        public string? CoverUrl { get; set; }

        public int PhotoCount { get; set; }
        public int AttendeeCount { get; set; }
    }
}
