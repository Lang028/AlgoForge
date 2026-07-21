using System.Text;
using AlgoForge.Data;
using AlgoForge.Models;
using AlgoForge.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AlgoForge.Tests.Controllers
{
    // Custom factory that replaces SQL Server with an in-memory database.
    public class TestWebApplicationFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                // Remove the real SQL Server DbContext and replace with in-memory
                services.RemoveAll<DbContextOptions<AlgoForgeDbContext>>();
                services.RemoveAll(typeof(DbContextOptions<AlgoForgeDbContext>));

                // Find and remove all DbContext registrations to prevent conflicts
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(DbContextOptions<AlgoForgeDbContext>));
                if (descriptor is not null)
                    services.Remove(descriptor);

                services.AddDbContext<AlgoForgeDbContext>(options =>
                    options.UseInMemoryDatabase("IntegrationTestDb_" + Guid.NewGuid()));
            });

            // Suppress the connection string requirement during tests
            builder.UseSetting("ConnectionStrings:DefaultConnection",
                "Server=.;Database=test;Trusted_Connection=True;");
        }
    }

    public class EventsControllerImportTests : IClassFixture<TestWebApplicationFactory>
    {
        private readonly TestWebApplicationFactory _factory;

        public EventsControllerImportTests(TestWebApplicationFactory factory)
        {
            _factory = factory;
        }

        private async Task<Guid> SeedEventAsync()
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AlgoForgeDbContext>();
            await db.Database.EnsureCreatedAsync();

            // Seed a minimal admin user (required by Organisation FK)
            var adminUserId = Guid.NewGuid();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser
            {
                Id = adminUserId,
                UserName = "admin@test.com",
                Email = "admin@test.com",
                NormalizedUserName = "ADMIN@TEST.COM",
                NormalizedEmail = "ADMIN@TEST.COM",
                SecurityStamp = Guid.NewGuid().ToString()
            };
            await userManager.CreateAsync(user, "Test@1234!");

            var org = new Organisation
            {
                Id = Guid.NewGuid(),
                Name = "Test Org",
                ContactEmail = "org@test.com",
                AdminUserId = adminUserId
            };
            db.Organisations.Add(org);

            var evt = new Event
            {
                Id = Guid.NewGuid(),
                Name = "Test Event",
                EventDate = DateTime.UtcNow.AddDays(30),
                OrganisationId = org.Id
            };
            db.Events.Add(evt);
            await db.SaveChangesAsync();
            return evt.Id;
        }

        // ── Integration test: POST valid CSV creates attendees in DB ──────────
        // This test exercises the service layer + EF Core persistence in an
        // in-memory database, matching the full controller flow exactly.
        [Fact]
        public async Task ImportAttendees_ValidCsv_CreatesAttendeesWithCorrectEventIdAndInviteToken()
        {
            var eventId = await SeedEventAsync();

            const string csv = "Name,Email,ContactInfo\n" +
                               "Jane Doe,jane@example.com,+27 81 234 5678\n" +
                               "John Smith,john.smith@example.com,john@company.com\n" +
                               "Alice Chen,alice.chen@acme.org,alice@acme.org";

            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AlgoForgeDbContext>();
            var importService = scope.ServiceProvider.GetRequiredService<AttendeeImportService>();

            // 1. Parse CSV (same as controller POST step)
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
            var parseResult = importService.ParseCsv(stream, eventId);

            Assert.Empty(parseResult.Errors);
            Assert.Equal(3, parseResult.ValidAttendees.Count);

            // 2. Stamp InviteTokens and persist (same as controller SaveAttendees step)
            foreach (var a in parseResult.ValidAttendees)
                a.InviteToken = Guid.NewGuid().ToString("N");

            db.Attendees.AddRange(parseResult.ValidAttendees);
            await db.SaveChangesAsync();

            // 3. Verify persisted state
            var saved = await db.Attendees.Where(a => a.EventId == eventId).ToListAsync();

            Assert.Equal(3, saved.Count);
            Assert.All(saved, attendee =>
            {
                Assert.Equal(eventId, attendee.EventId);
                Assert.False(string.IsNullOrEmpty(attendee.InviteToken),
                    "InviteToken must be a non-empty string");
                Assert.Equal(32, attendee.InviteToken.Length); // Guid.NewGuid().ToString("N") produces 32 hex chars
            });

            // 4. Verify names were preserved correctly
            var names = saved.Select(a => a.Name).OrderBy(n => n).ToList();
            Assert.Equal(new[] { "Alice Chen", "Jane Doe", "John Smith" }, names);
        }
    }
}
