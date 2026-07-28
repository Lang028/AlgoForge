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

        // Which role the person intends to use this session for. Actual permissions
        // still come from EventMembership -- this shapes what the dashboard and
        // navigation show, it never grants access by itself.
        [Required(ErrorMessage = "Please choose the role you want to sign in as.")]
        [Display(Name = "Sign in as")]
        public EventRole? SelectedRole { get; set; }
    }
}
