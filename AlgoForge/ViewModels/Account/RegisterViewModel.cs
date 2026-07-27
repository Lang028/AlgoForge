using System.ComponentModel.DataAnnotations;

namespace AlgoForge.ViewModels.Account
{
    // Registration creates an account and nothing else. It deliberately carries no event
    // and no role: those arrive only by creating an event, accepting an invitation, or
    // claiming an attendee record from an emailed token.
    //
    // The previous version let the registrant pick any event in the database and grant
    // themselves Coordinator or Photographer on it. That was written when nothing read
    // EventMembership, so it looked harmless; the moment the access rules went in it became
    // a way to hand yourself the keys to someone else's event.
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
    }
}
