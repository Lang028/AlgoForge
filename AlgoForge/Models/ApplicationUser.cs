using Microsoft.AspNetCore.Identity;

namespace AlgoForge.Models
{
    public class ApplicationUser : IdentityUser<Guid>
    {
        public string DisplayName { get; set; } = string.Empty;
    }
}
