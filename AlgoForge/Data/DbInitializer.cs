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
            if (await db.Events.AnyAsync())
            {
                return;
            }

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

            var demoEvent = new Event
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
    }
}
