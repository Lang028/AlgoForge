using System.ComponentModel.DataAnnotations;

namespace AlgoForge.ViewModels.Events
{
    public class CreateEventViewModel
    {
        [Required]
        [Display(Name = "Organisation")]
        public Guid OrganisationId { get; set; }

        [Required, MaxLength(200)]
        [Display(Name = "Event name")]
        public string Name { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Event date")]
        [DataType(DataType.Date)]
        public DateTime EventDate { get; set; } = DateTime.Today;

        [Display(Name = "Attendees must confirm suggested tags")]
        public bool TagConfirmationRequired { get; set; } = true;
    }
}
