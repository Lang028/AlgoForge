using AlgoForge.Models;

namespace AlgoForge.ViewModels.Admin
{
    // Everything here is an aggregate or event-level metadata. No user names, emails,
    // contact details, or per-person rows -- the admin console monitors activity, it
    // does not browse people.
    public class AdminDashboardViewModel
    {
        public int TotalUsers { get; set; }
        public int TotalOrganisations { get; set; }
        public int TotalEvents { get; set; }
        public int TotalPhotos { get; set; }
        public int TotalAttendees { get; set; }
        public int TotalConnections { get; set; }
        public int PendingConnections { get; set; }

        public int TotalTags { get; set; }
        public int SuggestedTags { get; set; }
        public int ConfirmedTags { get; set; }
        public int RejectedTags { get; set; }

        /// <summary>Uploads per day for the last 14 days, oldest first.</summary>
        public List<AdminUploadsPerDay> UploadsLast14Days { get; set; } = new();

        public List<EventOverviewRow> Events { get; set; } = new();
    }

    public class AdminUploadsPerDay
    {
        public DateTime Day { get; set; }
        public int Count { get; set; }
    }

    /// <summary>One event as the admin sees it: metadata and counts only, no way in.</summary>
    public class EventOverviewRow
    {
        public string Name { get; set; } = string.Empty;
        public string OrganisationName { get; set; } = string.Empty;
        public DateTime EventDate { get; set; }
        public EventStatus Status { get; set; }
        public int PhotoCount { get; set; }
        public int AttendeeCount { get; set; }
        public int TagCount { get; set; }
    }
}
