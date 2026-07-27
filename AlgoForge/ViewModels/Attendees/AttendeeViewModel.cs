using System.ComponentModel.DataAnnotations;

namespace AlgoForge.ViewModels.Attendees
{
    public class AttendeeListViewModel
    {
        public int EventId { get; set; }

        public string EventName { get; set; } = string.Empty;

        public List<AttendeeRow> Attendees { get; set; } = new();
    }

    public class AttendeeRow
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public bool IsClaimed { get; set; }
    }

    // Serves both Add and Edit. Id is 0 on Add, populated on Edit.
    public class AttendeeFormViewModel
    {
        public int EventId { get; set; }

        public int Id { get; set; }

        [Required]
        public string Name { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        // Deliberately required even though nothing else in the schema enforces it.
        [Required]
        [Display(Name = "Contact info")]
        public string ContactInfo { get; set; } = string.Empty;

        // Display only
        public string EventName { get; set; } = string.Empty;

        public bool IsClaimed { get; set; }
    }
}

