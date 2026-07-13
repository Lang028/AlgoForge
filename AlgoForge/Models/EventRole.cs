namespace AlgoForge.Models
{
    // D1: Coordinator and Photographer are separate roles with disjoint permissions.
    public enum EventRole
    {
        Coordinator,
        Photographer,
        Attendee,
        Delegate
    }
}
