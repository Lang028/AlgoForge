using AlgoForge.Models;

namespace AlgoForge.Services.Authorization
{
    // The single answer to "what may this user do inside this event" (D2).
    //
    // Every event-scoped action goes through here -- see
    // docs/ORGANISATIONS_AND_ACCESS_PLAN.md §4. Keeping it to one implementation is the
    // whole point: the consent rules are only testable while exactly one place decides
    // access, however many clauses that decision grows later.
    public interface IEventAccessService
    {
        // Empty means no access at all. A user may legitimately hold several roles on one
        // event: a photographer running a small event alone is both its Coordinator and its
        // Photographer, and D1 keeps those permissions disjoint. So this is a set, never a
        // single value.
        Task<IReadOnlySet<EventRole>> ResolveRolesAsync(
            Guid userId, Guid eventId, CancellationToken cancellationToken = default);

        Task<bool> HasAnyRoleAsync(
            Guid userId,
            Guid eventId,
            IReadOnlyCollection<EventRole> roles,
            CancellationToken cancellationToken = default);
    }
}
