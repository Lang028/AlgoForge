using System.ComponentModel.DataAnnotations;

namespace AlgoForge.Models
{
    public class Event
    {
        public int Id { get; set; }

        public int OrganisationId { get; set; }
        public Organisation Organisation { get; set; } = null!;

        [Required, MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        public DateTime EventDate { get; set; }

        public EventStatus Status { get; set; } = EventStatus.Draft;

        // "Trusted" small event vs "Large" event — governs TagConfirmationMode default
        public TagConfirmationMode TagConfirmationMode { get; set; } = TagConfirmationMode.RequireConfirmation;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<EventMembership> Memberships { get; set; } = new List<EventMembership>();
        public ICollection<Album> Albums { get; set; } = new List<Album>();
        public ICollection<Invitation> Invitations { get; set; } = new List<Invitation>();
        public ICollection<FaceCluster> FaceClusters { get; set; } = new List<FaceCluster>();

        public bool IsUploadWindowOpen => Status == EventStatus.Live;
        public bool AreConnectionsOpen => Status == EventStatus.PostEvent || Status == EventStatus.Archived;
    }
}
