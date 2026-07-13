namespace AlgoForge.Models
{
    public class Organisation
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string ContactEmail { get; set; } = string.Empty;

        // The user who administers this org (org admin has same permissions as photographer)
        public Guid AdminUserId { get; set; }
        public ApplicationUser? AdminUser { get; set; }

        public ICollection<Event> Events { get; set; } = new List<Event>();
    }
}
