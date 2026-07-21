using AlgoForge.Services;

namespace AlgoForge.ViewModels.Events
{
    public class ImportAttendeesViewModel
    {
        public Guid EventId { get; set; }
        public string EventName { get; set; } = string.Empty;
        public AttendeeImportResult? ParseResult { get; set; }
    }

    /// <summary>
    /// Lightweight model used to round-trip valid attendees back via hidden form fields
    /// when the coordinator chooses "Import valid rows anyway" after seeing row errors.
    /// </summary>
    public class AttendeeInputModel
    {
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string ContactInfo { get; set; } = string.Empty;
    }
}
