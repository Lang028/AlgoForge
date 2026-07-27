using AlgoForge.Data;
using AlgoForge.Models;
using AlgoForge.Services.Authorization;
using Microsoft.EntityFrameworkCore;

namespace AlgoForge.Tests.Services
{
    // The access resolver is the whole permission spine (D2) -- every event-scoped action
    // in the app is one call to it away from being wide open, which is exactly what it was
    // before this existed. So it gets tests rather than trust.
    public class EventAccessServiceTests
    {
        private static AlgoForgeDbContext NewContext() =>
            new(new DbContextOptionsBuilder<AlgoForgeDbContext>()
                .UseInMemoryDatabase($"access-{Guid.NewGuid()}")
                .Options);

        private static async Task GrantAsync(
            AlgoForgeDbContext db, Guid eventId, Guid userId, params EventRole[] roles)
        {
            foreach (var role in roles)
            {
                db.EventMemberships.Add(new EventMembership
                {
                    Id = Guid.NewGuid(),
                    EventId = eventId,
                    UserId = userId,
                    Role = role
                });
            }

            await db.SaveChangesAsync();
        }

        [Fact]
        public async Task A_user_with_no_membership_resolves_to_nothing()
        {
            using var db = NewContext();
            var service = new EventAccessService(db);

            var roles = await service.ResolveRolesAsync(Guid.NewGuid(), Guid.NewGuid());

            Assert.Empty(roles);
        }

        [Fact]
        public async Task A_stranger_is_refused_every_role()
        {
            using var db = NewContext();
            var eventId = Guid.NewGuid();
            await GrantAsync(db, eventId, Guid.NewGuid(), EventRole.Coordinator);

            var service = new EventAccessService(db);
            var stranger = Guid.NewGuid();

            foreach (var role in Enum.GetValues<EventRole>())
            {
                Assert.False(await service.HasAnyRoleAsync(stranger, eventId, new[] { role }));
            }
        }

        // The solo-photographer case, and the reason ResolveRolesAsync returns a set. D1
        // keeps Coordinator and Photographer disjoint; someone running a small event alone
        // needs both, so they hold two rows rather than a weakened role.
        [Fact]
        public async Task An_event_creator_holds_both_staff_roles()
        {
            using var db = NewContext();
            var eventId = Guid.NewGuid();
            var creator = Guid.NewGuid();
            await GrantAsync(db, eventId, creator, EventRole.Coordinator, EventRole.Photographer);

            var service = new EventAccessService(db);
            var roles = await service.ResolveRolesAsync(creator, eventId);

            Assert.Equal(2, roles.Count);
            Assert.Contains(EventRole.Coordinator, roles);
            Assert.Contains(EventRole.Photographer, roles);
            Assert.True(await service.HasAnyRoleAsync(creator, eventId, new[] { EventRole.Photographer }));
        }

        [Fact]
        public async Task An_attendee_cannot_upload_or_identify()
        {
            using var db = NewContext();
            var eventId = Guid.NewGuid();
            var attendee = Guid.NewGuid();
            await GrantAsync(db, eventId, attendee, EventRole.Attendee);

            var service = new EventAccessService(db);

            Assert.False(await service.HasAnyRoleAsync(
                attendee, eventId, new[] { EventRole.Photographer }));
            Assert.False(await service.HasAnyRoleAsync(
                attendee, eventId, new[] { EventRole.Coordinator, EventRole.Photographer }));
            Assert.True(await service.HasAnyRoleAsync(
                attendee, eventId, new[] { EventRole.Attendee }));
        }

        // Membership is per event, not global (D2). Holding Coordinator at one event must
        // buy nothing at another -- this is the property the whole multi-organisation model
        // rests on.
        [Fact]
        public async Task Membership_does_not_leak_across_events()
        {
            using var db = NewContext();
            var mine = Guid.NewGuid();
            var theirs = Guid.NewGuid();
            var user = Guid.NewGuid();
            await GrantAsync(db, mine, user, EventRole.Coordinator, EventRole.Photographer);

            var service = new EventAccessService(db);

            Assert.NotEmpty(await service.ResolveRolesAsync(user, mine));
            Assert.Empty(await service.ResolveRolesAsync(user, theirs));
        }

        [Fact]
        public async Task An_empty_role_list_never_grants_access()
        {
            using var db = NewContext();
            var eventId = Guid.NewGuid();
            var coordinator = Guid.NewGuid();
            await GrantAsync(db, eventId, coordinator, EventRole.Coordinator);

            var service = new EventAccessService(db);

            Assert.False(await service.HasAnyRoleAsync(coordinator, eventId, Array.Empty<EventRole>()));
        }
    }
}
