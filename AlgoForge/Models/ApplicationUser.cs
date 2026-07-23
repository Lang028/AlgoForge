using Microsoft.AspNetCore.Identity;

namespace AlgoForge.Models
{
    // Extends ASP.NET Identity's user with the profile fields Geeked On needs.
    public class ApplicationUser : IdentityUser
    {
        public string FullName { get; set; } = string.Empty;
        public string? ProfilePhotoBlobUrl { get; set; }
        public string? Bio { get; set; }

        // Privacy: whether this user's profile can be discovered by other
        // attendees before a connection request is accepted.
        public bool ProfileDiscoverable { get; set; } = true;

        public ICollection<EventMembership> Memberships { get; set; } = new List<EventMembership>();
    }
}
