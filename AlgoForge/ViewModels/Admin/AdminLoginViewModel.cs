using System.ComponentModel.DataAnnotations;

namespace AlgoForge.ViewModels.Admin
{
    public class AdminLoginViewModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;
    }
}
