using System.ComponentModel.DataAnnotations;

namespace AlgoForge.ViewModels.Account
{
    public class ResetPasswordViewModel
    {
        // Carried through the form rather than the query string on the post, so the token
        // does not end up in browser history or a referrer header on the page that uses it.
        public string UserId { get; set; } = string.Empty;

        public string Token { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        [Display(Name = "New password")]
        public string Password { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        [Display(Name = "Confirm new password")]
        [Compare(nameof(Password), ErrorMessage = "The passwords don't match.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}
