using AlgoForge.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AlgoForge.Data
{
    // Dev-only bootstrap data. There's no Organisation/Event creation UI yet (that's
    // teammate work), so this seeds one demo org + event on startup purely so the photo
    // upload/face pipeline has somewhere to attach photos to during local testing.
    public static class DbInitializer
    {
        public static async Task SeedAsync(AlgoForgeDbContext db, UserManager<ApplicationUser> userManager)
        {
            var demoEvent = await db.Events.FirstOrDefaultAsync(e => e.Name == "Demo Event");

            if (demoEvent is null)
            {
                var seedAdmin = await userManager.FindByEmailAsync("seed-admin@algoforge.local");
                if (seedAdmin is null)
                {
                    seedAdmin = new ApplicationUser
                    {
                        UserName = "seed-admin@algoforge.local",
                        Email = "seed-admin@algoforge.local",
                        DisplayName = "Seed Admin",
                        EmailConfirmed = true
                    };
                    await userManager.CreateAsync(seedAdmin, "SeedAdmin123!");
                }

                var organisation = new Organisation
                {
                    Id = Guid.NewGuid(),
                    Name = "Demo Org",
                    ContactEmail = "seed-admin@algoforge.local",
                    AdminUserId = seedAdmin.Id
                };
                db.Organisations.Add(organisation);

                demoEvent = new Event
                {
                    Id = Guid.NewGuid(),
                    Name = "Demo Event",
                    EventDate = DateTime.UtcNow,
                    OrganisationId = organisation.Id,
                    Status = EventStatus.Live
                };
                db.Events.Add(demoEvent);

                await db.SaveChangesAsync();
            }

            // Pre-claimed so the demo can show the attendee-side consent review (Tags/{eventId})
            // without the email/claim flow, which isn't built yet (OPEN-6). Kept as its own
            // gate (not folded into the "any events exist" check above) so it still gets
            // created on databases that were seeded before this attendee existed.
            var demoAttendeeUser = await userManager.FindByEmailAsync("demo-attendee@algoforge.local");
            if (demoAttendeeUser is null)
            {
                demoAttendeeUser = new ApplicationUser
                {
                    UserName = "demo-attendee@algoforge.local",
                    Email = "demo-attendee@algoforge.local",
                    DisplayName = "Demo Attendee",
                    EmailConfirmed = true
                };
                await userManager.CreateAsync(demoAttendeeUser, "DemoAttendee123!");
            }

            var demoAttendee = await db.Attendees
                .FirstOrDefaultAsync(a => a.EventId == demoEvent.Id && a.Email == "demo-attendee@algoforge.local");
            if (demoAttendee is null)
            {
                db.Attendees.Add(new Attendee
                {
                    Id = Guid.NewGuid(),
                    EventId = demoEvent.Id,
                    Name = "Demo Attendee",
                    Email = "demo-attendee@algoforge.local",
                    ContactInfo = string.Empty,
                    InviteToken = Guid.NewGuid().ToString("N"),
                    ClaimedByUserId = demoAttendeeUser.Id
                });
                await db.SaveChangesAsync();
            }
        }
    }
}
