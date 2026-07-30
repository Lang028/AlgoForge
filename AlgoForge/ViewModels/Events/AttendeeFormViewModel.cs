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

        // Nullable on purpose: removing [Required] is not enough on its own, because the
        // project builds with <Nullable>enable</Nullable> and ASP.NET Core treats a
        // non-nullable reference type as implicitly required. A photographer adding a
        // walk-in often doesn't have a phone number yet, so this has to be genuinely
        // optional -- see RegisterViewModel.DisplayName for the same trap.
        [Display(Name = "Contact info")]
        public string? ContactInfo { get; set; }

        // Display only
        public string EventName { get; set; } = string.Empty;

        public bool IsClaimed { get; set; }
    }
}
