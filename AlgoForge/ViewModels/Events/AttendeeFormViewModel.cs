using System.ComponentModel.DataAnnotations;

namespace AlgoForge.ViewModels.Events
{
    /// <summary>
    /// Serves both AddAttendee and EditAttendee. AttendeeId is null on Add.
    /// </summary>
    public class AttendeeFormViewModel
    {
        public Guid EventId { get; set; }

        public Guid? AttendeeId { get; set; }

        [Required]
        public string Name { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Contact info")]
        public string ContactInfo { get; set; } = string.Empty;

        // Display only
        public string EventName { get; set; } = string.Empty;

        public bool IsClaimed { get; set; }
    }
}
