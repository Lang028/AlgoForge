using System.ComponentModel.DataAnnotations;

namespace AlgoForge.ViewModels.Account
{
    public class ForgotPasswordViewModel
    {
        [Required]
        [EmailAddress]
        [Display(Name = "Email address")]
        public string Email { get; set; } = string.Empty;
    }
}
