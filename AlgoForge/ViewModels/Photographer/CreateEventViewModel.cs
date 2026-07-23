using System.ComponentModel.DataAnnotations;

namespace AlgoForge.ViewModels.Photographer
{
    public class CreateEventViewModel
    {
        [Required]
        [Display(Name = "Organisation")]
        public int OrganisationId { get; set; }

        public List<(int Id, string Name)> Organisations { get; set; } = new();

        [Required, MaxLength(200)]
        [Display(Name = "Event name")]
        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        [Required]
        [Display(Name = "Event date")]
        [DataType(DataType.Date)]
        public DateTime EventDate { get; set; } = DateTime.Today;

        [Display(Name = "Tag confirmation")]
        public bool RequireAttendeeConfirmation { get; set; } = true;
    }

    public class JoinEventViewModel
    {
        [Required]
        [Display(Name = "Invitation code")]
        public string InvitationToken { get; set; } = string.Empty;
    }
}
