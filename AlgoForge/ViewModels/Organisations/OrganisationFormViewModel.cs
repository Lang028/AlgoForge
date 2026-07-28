using System.ComponentModel.DataAnnotations;

namespace AlgoForge.ViewModels.Organisations
{
    // Serves both Create and Edit. Id is Guid.Empty on Create, populated on Edit.
    public class OrganisationFormViewModel
    {
        public Guid Id { get; set; }

        [Required]
        public string Name { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        [Display(Name = "Contact email")]
        public string ContactEmail { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Admin user")]
        public Guid AdminUserId { get; set; }
    }
}
