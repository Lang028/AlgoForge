using System.ComponentModel.DataAnnotations;

namespace AlgoForge.ViewModels.Photographer
{
    public class FaceClusterListViewModel
    {
        public int EventId { get; set; }
        public string EventName { get; set; } = string.Empty;
        public List<FaceClusterSummary> IdentifiedClusters { get; set; } = new();
        public List<FaceClusterSummary> UnidentifiedClusters { get; set; } = new();
    }

    public class FaceClusterSummary
    {
        public int ClusterId { get; set; }
        public int DetectionCount { get; set; }
        public string? RepresentativeThumbnailUrl { get; set; }
        public string? IdentifiedAttendeeName { get; set; }
    }

    public class IdentifyClusterViewModel
    {
        public int ClusterId { get; set; }
        public int EventId { get; set; }
        public List<string> SampleThumbnailUrls { get; set; } = new();

        [Display(Name = "Which invited attendee is this?")]
        public int SelectedEventMembershipId { get; set; }

        public List<(int MembershipId, string AttendeeName)> InvitedAttendees { get; set; } = new();

        // "Or invite someone new" path — Identify Face Cluster (Create Attendee Profile)
        [EmailAddress]
        [Display(Name = "Or invite a new attendee by email")]
        public string? NewAttendeeEmail { get; set; }
    }

    public class EditAttendeeProfileViewModel
    {
        public int EventMembershipId { get; set; }

        [Required, MaxLength(100)]
        [Display(Name = "Full name")]
        public string FullName { get; set; } = string.Empty;

        public string? Bio { get; set; }
    }
}
