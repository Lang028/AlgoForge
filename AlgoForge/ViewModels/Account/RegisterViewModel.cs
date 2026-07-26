using System.ComponentModel.DataAnnotations;
using AlgoForge.Models;

namespace AlgoForge.ViewModels.Account
{
    public class RegisterViewModel
    {
        [Required]
        [Display(Name = "Full name")]
        public string DisplayName { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
        [Display(Name = "Confirm password")]
        public string ConfirmPassword { get; set; } = string.Empty;

        // Demo-only: lets a self-registered account act as a distinct persona for a given
        // event. No permission enforcement reads this yet (D2/EventMembership policies
        // aren't built) -- it just creates the EventMembership record so different logins
        // have something real behind them during a walkthrough. Attendee/Delegate aren't
        // offered here on purpose: those come from the claim/invite flow (D3/D5), not
        // self-registration.
        [Required]
        [Display(Name = "Event")]
        public Guid EventId { get; set; }

        [Required]
        [Display(Name = "Role for this event")]
        public EventRole Role { get; set; } = EventRole.Photographer;
    }
}
