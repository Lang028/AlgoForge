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
        public const string SystemAdminRole = "SystemAdmin";
        public const string SystemAdminEmail = "admin@geeked.ac.za";

        // Every password below is a development convenience and is in this repository's
        // git history permanently, so none of them may ever protect anything real. The
        // admin password is read from configuration first (Seed:AdminPassword, set through
        // user secrets or an app setting) precisely so that a deployment which does need a
        // real admin account can supply one that was never committed. The literal is only
        // the local-development fallback.
        public static async Task SeedAsync(
            AlgoForgeDbContext db,
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole<Guid>> roleManager,
            string? adminPassword = null)
        {
            // Platform admin: monitors the system through the read-only admin console and
            // never participates in events. It is an Identity role, deliberately separate
            // from EventRole -- RequireEventRole never matches it, so event content
            // (galleries, attendee lists, tags) stays out of reach by construction.
            if (!await roleManager.RoleExistsAsync(SystemAdminRole))
            {
                await roleManager.CreateAsync(new IdentityRole<Guid>(SystemAdminRole));
            }

            var systemAdmin = await userManager.FindByEmailAsync(SystemAdminEmail);
            if (systemAdmin is null)
            {
                systemAdmin = new ApplicationUser
                {
                    UserName = SystemAdminEmail,
                    Email = SystemAdminEmail,
                    DisplayName = "System Admin",
                    EmailConfirmed = true
                };
                var result = await userManager.CreateAsync(
                    systemAdmin,
                    string.IsNullOrWhiteSpace(adminPassword) ? "Geeked@2026" : adminPassword);

                if (!result.Succeeded)
                {
                    // Silently carrying on would leave an admin account that exists but
                    // cannot be signed into, which is far harder to diagnose later than
                    // failing here -- a supplied password that trips the Identity rules is
                    // the likely cause.
                    throw new InvalidOperationException(
                        "Could not create the system admin: " +
                        string.Join("; ", result.Errors.Select(e => e.Description)));
                }
            }
            if (!await userManager.IsInRoleAsync(systemAdmin, SystemAdminRole))
            {
                await userManager.AddToRoleAsync(systemAdmin, SystemAdminRole);
            }

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

            // Memberships are what grant access now that the app actually reads them, so the
            // seeded personas need them or the demo event is unreachable to everyone --
            // including the admin who owns it. The seed admin gets both staff roles because
            // D1 keeps them disjoint and a one-person demo has to do both jobs.
            var seedAdminUser = await userManager.FindByEmailAsync("seed-admin@algoforge.local");
            if (seedAdminUser is not null)
            {
                await GrantAsync(db, demoEvent.Id, seedAdminUser.Id, EventRole.Coordinator);
                await GrantAsync(db, demoEvent.Id, seedAdminUser.Id, EventRole.Photographer);
            }

            // A photographer of their own, holding *only* the Photographer role. The seed
            // admin above wears both hats, which is realistic for a one-person event but
            // hides the thing D1 actually specifies: the two roles are disjoint. With this
            // persona you can show a photographer who can upload but cannot open the
            // attendee list, and a coordinator who can do the reverse.
            var demoPhotographer = await userManager.FindByEmailAsync("demo-photographer@algoforge.local");
            if (demoPhotographer is null)
            {
                demoPhotographer = new ApplicationUser
                {
                    UserName = "demo-photographer@algoforge.local",
                    Email = "demo-photographer@algoforge.local",
                    DisplayName = "Demo Photographer",
                    EmailConfirmed = true
                };
                await userManager.CreateAsync(demoPhotographer, "DemoPhotographer123!");
            }

            await GrantAsync(db, demoEvent.Id, demoPhotographer.Id, EventRole.Photographer);

            await GrantAsync(db, demoEvent.Id, demoAttendeeUser.Id, EventRole.Attendee);
            await db.SaveChangesAsync();
        }

        private static async Task GrantAsync(
            AlgoForgeDbContext db, Guid eventId, Guid userId, EventRole role)
        {
            var exists = await db.EventMemberships.AnyAsync(
                m => m.EventId == eventId && m.UserId == userId && m.Role == role);

            if (!exists)
            {
                db.EventMemberships.Add(new EventMembership
                {
                    Id = Guid.NewGuid(),
                    EventId = eventId,
                    UserId = userId,
                    Role = role
                });
            }
        }
    }
}
