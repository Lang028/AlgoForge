using System.ComponentModel.DataAnnotations;
using AlgoForge.Models;

namespace AlgoForge.ViewModels.Account
{
    // NOTE: I don't have the original file, so Email/Password/RememberMe are
    // reconstructed from what Login.cshtml already binds to. If your real
    // LoginViewModel has extra fields, add them back in -- only SelectedRole
    // is new here.
    public class LoginViewModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Display(Name = "Remember me")]
        public bool RememberMe { get; set; }

        // Optional, and no longer asked for on the sign-in form: the role chosen at sign-up
        // is remembered instead, and falls back to whatever the account actually holds.
        // Kept on the model so a link can still deep-link into a particular view of the app.
        // Actual permissions have never come from here -- they come from EventMembership.
        [Display(Name = "Sign in as")]
        public EventRole? SelectedRole { get; set; }
    }
}
