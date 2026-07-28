using AlgoForge.Controllers;
using AlgoForge.Data;
using AlgoForge.Models;
using AlgoForge.Services;
using AlgoForge.ViewModels.Events;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AlgoForge.Tests.Controllers
{
    // Exercises the manual Add/Edit attendee actions (ported from the Anele branch):
    // one-email-per-event, invite token stamped on create, token rotated only when an
    // unclaimed attendee's email changes, and edit never crossing event boundaries.
    //
    // Note: the factory's in-memory database name is minted per DI scope, so each test
    // seeds, acts, and asserts inside a single scope (same pattern as the import tests).
    public class EventsControllerAttendeeCrudTests : IClassFixture<TestWebApplicationFactory>
    {
        private readonly TestWebApplicationFactory _factory;

        public EventsControllerAttendeeCrudTests(TestWebApplicationFactory factory)
        {
            _factory = factory;
        }

        private sealed class NullTempDataProvider : ITempDataProvider
        {
            public IDictionary<string, object?> LoadTempData(HttpContext context) =>
                new Dictionary<string, object?>();

            public void SaveTempData(HttpContext context, IDictionary<string, object?> values) { }
        }

        private static EventsController BuildController(IServiceScope scope)
        {
            var controller = new EventsController(
                scope.ServiceProvider.GetRequiredService<AlgoForgeDbContext>(),
                scope.ServiceProvider.GetRequiredService<AttendeeImportService>(),
                scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(),
                scope.ServiceProvider.GetRequiredService<IEmailSender>());

            controller.TempData = new TempDataDictionary(new DefaultHttpContext(), new NullTempDataProvider());
            return controller;
        }

        private static async Task<Guid> SeedEventAsync(IServiceScope scope)
        {
            var db = scope.ServiceProvider.GetRequiredService<AlgoForgeDbContext>();
            await db.Database.EnsureCreatedAsync();

            var adminUserId = Guid.NewGuid();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser
            {
                Id = adminUserId,
                UserName = $"admin-{adminUserId:N}@test.com",
                Email = $"admin-{adminUserId:N}@test.com",
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

        private static async Task<Attendee> SeedAttendeeAsync(IServiceScope scope, Guid eventId,
            string email, Guid? claimedBy = null)
        {
            var db = scope.ServiceProvider.GetRequiredService<AlgoForgeDbContext>();
            var attendee = new Attendee
            {
                Id = Guid.NewGuid(),
                EventId = eventId,
                Name = "Seeded Person",
                Email = email,
                ContactInfo = "+27 81 000 0000",
                InviteToken = Guid.NewGuid().ToString("N"),
                ClaimedByUserId = claimedBy
            };
            db.Attendees.Add(attendee);
            await db.SaveChangesAsync();
            return attendee;
        }

        [Fact]
        public async Task AddAttendee_CreatesRecordWithInviteToken()
        {
            using var scope = _factory.Services.CreateScope();
            var eventId = await SeedEventAsync(scope);
            var controller = BuildController(scope);

            var result = await controller.AddAttendee(eventId, new AttendeeFormViewModel
            {
                Name = "Jane Doe",
                Email = "jane@example.com",
                ContactInfo = "+27 81 234 5678"
            });

            Assert.IsType<RedirectToActionResult>(result);

            var db = scope.ServiceProvider.GetRequiredService<AlgoForgeDbContext>();
            var saved = await db.Attendees.SingleAsync(a => a.EventId == eventId && a.Email == "jane@example.com");
            Assert.Equal("Jane Doe", saved.Name);
            Assert.Equal(32, saved.InviteToken.Length);
            Assert.Null(saved.ClaimedByUserId);
        }

        [Fact]
        public async Task AddAttendee_RejectsDuplicateEmailWithinEvent()
        {
            using var scope = _factory.Services.CreateScope();
            var eventId = await SeedEventAsync(scope);
            await SeedAttendeeAsync(scope, eventId, "dup@example.com");
            var controller = BuildController(scope);

            var result = await controller.AddAttendee(eventId, new AttendeeFormViewModel
            {
                Name = "Second Person",
                Email = "dup@example.com",
                ContactInfo = "n/a"
            });

            Assert.IsType<ViewResult>(result);
            Assert.False(controller.ModelState.IsValid);

            var db = scope.ServiceProvider.GetRequiredService<AlgoForgeDbContext>();
            Assert.Equal(1, await db.Attendees.CountAsync(a => a.EventId == eventId && a.Email == "dup@example.com"));
        }

        [Fact]
        public async Task AddAttendee_SameEmailAllowedAcrossDifferentEvents()
        {
            using var scope = _factory.Services.CreateScope();
            var eventA = await SeedEventAsync(scope);
            var eventB = await SeedEventAsync(scope);
            await SeedAttendeeAsync(scope, eventA, "shared@example.com");
            var controller = BuildController(scope);

            var result = await controller.AddAttendee(eventB, new AttendeeFormViewModel
            {
                Name = "Same Email Other Event",
                Email = "shared@example.com",
                ContactInfo = "n/a"
            });

            Assert.IsType<RedirectToActionResult>(result);
        }

        [Fact]
        public async Task EditAttendee_EmailChangeOnUnclaimedAttendee_RotatesInviteToken()
        {
            using var scope = _factory.Services.CreateScope();
            var eventId = await SeedEventAsync(scope);
            var attendee = await SeedAttendeeAsync(scope, eventId, "before@example.com");
            var originalToken = attendee.InviteToken;
            var controller = BuildController(scope);

            var result = await controller.EditAttendee(eventId, attendee.Id, new AttendeeFormViewModel
            {
                Name = "Seeded Person",
                Email = "after@example.com",
                ContactInfo = "+27 81 000 0000"
            });

            Assert.IsType<RedirectToActionResult>(result);

            var db = scope.ServiceProvider.GetRequiredService<AlgoForgeDbContext>();
            var saved = await db.Attendees.SingleAsync(a => a.Id == attendee.Id);
            Assert.Equal("after@example.com", saved.Email);
            Assert.NotEqual(originalToken, saved.InviteToken);
        }

        [Fact]
        public async Task EditAttendee_EmailChangeOnClaimedAttendee_KeepsToken()
        {
            using var scope = _factory.Services.CreateScope();
            var eventId = await SeedEventAsync(scope);

            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var claimer = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = "claimer@test.com",
                Email = "claimer@test.com",
                SecurityStamp = Guid.NewGuid().ToString()
            };
            await userManager.CreateAsync(claimer, "Test@1234!");

            var attendee = await SeedAttendeeAsync(scope, eventId, "claimed@example.com", claimedBy: claimer.Id);
            var originalToken = attendee.InviteToken;
            var controller = BuildController(scope);

            var result = await controller.EditAttendee(eventId, attendee.Id, new AttendeeFormViewModel
            {
                Name = "Seeded Person",
                Email = "new-address@example.com",
                ContactInfo = "+27 81 000 0000"
            });

            Assert.IsType<RedirectToActionResult>(result);

            var db = scope.ServiceProvider.GetRequiredService<AlgoForgeDbContext>();
            var saved = await db.Attendees.SingleAsync(a => a.Id == attendee.Id);
            Assert.Equal(originalToken, saved.InviteToken);
        }

        [Fact]
        public async Task EditAttendee_AttendeeFromAnotherEvent_IsNotFound()
        {
            using var scope = _factory.Services.CreateScope();
            var eventA = await SeedEventAsync(scope);
            var eventB = await SeedEventAsync(scope);
            var attendeeInB = await SeedAttendeeAsync(scope, eventB, "elsewhere@example.com");
            var controller = BuildController(scope);

            var result = await controller.EditAttendee(eventA, attendeeInB.Id, new AttendeeFormViewModel
            {
                Name = "Hijack",
                Email = "hijack@example.com",
                ContactInfo = "n/a"
            });

            Assert.IsType<NotFoundResult>(result);
        }
    }
}
