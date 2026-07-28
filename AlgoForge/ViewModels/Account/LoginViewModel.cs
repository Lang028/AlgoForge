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

        // Which role the person intends to use this session for. Purely a
        // routing hint post-login -- actual permissions still come from
        // EventMembership, this just decides where to land them.
        [Display(Name = "Log in as")]
        public EventRole? SelectedRole { get; set; }
    }
}
