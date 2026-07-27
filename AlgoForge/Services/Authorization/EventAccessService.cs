using AlgoForge.Data;
using AlgoForge.Models;
using Microsoft.EntityFrameworkCore;

namespace AlgoForge.Services.Authorization
{
    public sealed class EventAccessService : IEventAccessService
    {
        private readonly AlgoForgeDbContext _db;

        public EventAccessService(AlgoForgeDbContext db)
        {
            _db = db;
        }

        public async Task<IReadOnlySet<EventRole>> ResolveRolesAsync(
            Guid userId, Guid eventId, CancellationToken cancellationToken = default)
        {
            var roles = await _db.EventMemberships
                .Where(m => m.EventId == eventId && m.UserId == userId)
                .Select(m => m.Role)
                .ToListAsync(cancellationToken);

            // Phase 2 seam (D24): an OrganisationMembership of Owner or Manager on this
            // event's organisation will resolve to an effective Coordinator, and it lands
            // *here* rather than as a second check at the call sites. One function answering
            // the access question is what keeps the consent rules provable.

            return roles.ToHashSet();
        }

        public async Task<bool> HasAnyRoleAsync(
            Guid userId,
            Guid eventId,
            IReadOnlyCollection<EventRole> roles,
            CancellationToken cancellationToken = default)
        {
            if (roles.Count == 0)
            {
                return false;
            }

            // Materialised so EF translates this to an IN clause rather than walking the
            // collection client-side.
            var wanted = roles.ToArray();

            return await _db.EventMemberships.AnyAsync(
                m => m.EventId == eventId && m.UserId == userId && wanted.Contains(m.Role),
                cancellationToken);
        }
    }
}
