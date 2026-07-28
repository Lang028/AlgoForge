using AlgoForge.Models;

namespace AlgoForge.ViewModels.Home
{
    public class DashboardViewModel
    {
        /// <summary>Role chosen at login ("ActiveRole" claim). Shapes what the dashboard shows;
        /// per-event permissions still come from EventMembership.</summary>
        public EventRole? ActiveRole { get; set; }

        public string UserDisplayName { get; set; } = string.Empty;

        /// <summary>Counts below are scoped to the events this user is a member of,
        /// not the whole database.</summary>
        public int TotalEvents { get; set; }
        public int TotalPhotos { get; set; }
        public int TotalAttendees { get; set; }
        public int TotalConnections { get; set; }
        public int PendingConnectionRequests { get; set; }

        /// <summary>Photo uploads per day for the last 7 days, oldest first.</summary>
        public List<UploadsPerDay> UploadsLast7Days { get; set; } = new();

        public List<RecentAttendeeRow> RecentAttendees { get; set; } = new();

        /// <summary>Organisations this user administers. Only populated for the Admin role.</summary>
        public List<OrganisationRow> MyOrganisations { get; set; } = new();

        public List<UpcomingEventRow> UpcomingEvents { get; set; } = new();

        public int TotalTags { get; set; }
        public int SuggestedTags { get; set; }
        public int ConfirmedTags { get; set; }
        public int RejectedTags { get; set; }
    }

    public class UploadsPerDay
    {
        public DateTime Day { get; set; }
        public int Count { get; set; }
    }

    public class OrganisationRow
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string ContactEmail { get; set; } = string.Empty;
        public int EventCount { get; set; }
    }

    public class RecentAttendeeRow
    {
        public string Name { get; set; } = string.Empty;
        public string EventName { get; set; } = string.Empty;
        public bool Claimed { get; set; }
        public DateTime EventDate { get; set; }
    }

    public class UpcomingEventRow
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public DateTime EventDate { get; set; }
        public EventStatus Status { get; set; }
    }
}
