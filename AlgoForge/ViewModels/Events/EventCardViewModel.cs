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

        /// <summary>The viewer's own role(s) in this event -- drives which action buttons the card offers.</summary>
        public List<EventRole> ViewerRoles { get; set; } = new();

        public bool CanUpload => ViewerRoles.Contains(EventRole.Photographer);
        public bool CanManageAttendees => ViewerRoles.Contains(EventRole.Coordinator);
        public bool CanHandOver => ViewerRoles.Contains(EventRole.Coordinator);
    }
}
