namespace AlgoForge.Models
{
    // D1: Coordinator and Photographer are separate roles with disjoint permissions.
    public enum EventRole
    {
        Coordinator,
        Photographer,
        Attendee,
        Delegate,

        // Organisation administrator. Unlike the four above it is not event-scoped and is
        // never stored in EventMembership -- it is backed by Organisation.AdminUserId.
        // Appended last so the existing integer values in the database don't shift.
        Admin
    }
}
