using System.ComponentModel.DataAnnotations;

namespace AlgoForge.ViewModels.Events
{
    public class CreateEventViewModel
    {
        // Optional. Left empty, the photographer runs the event themselves and can hand it
        // to an organisation later.
        [Display(Name = "Organisation")]
        public Guid? OrganisationId { get; set; }

        [Required, MaxLength(200)]
        [Display(Name = "Event name")]
        public string Name { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Event date")]
        [DataType(DataType.Date)]
        public DateTime EventDate { get; set; } = DateTime.Today;

        [Display(Name = "Attendees must confirm suggested tags")]
        public bool TagConfirmationRequired { get; set; } = true;

        // Asked here and nowhere else: Event.GalleryMode cannot be changed once the event
        // exists, so this is the only moment it is ever offered.
        [Display(Name = "How should people see these photos?")]
        public AlgoForge.Models.EventGalleryMode GalleryMode { get; set; }
            = AlgoForge.Models.EventGalleryMode.Consent;
    }
}
