using System.Reflection;
using AlgoForge.Controllers;
using AlgoForge.Models;
using AlgoForge.Services.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AlgoForge.Tests.Controllers
{
    // The one test that stops this class of bug coming back.
    //
    // Before the access rules went in, every controller action that took an eventId was
    // reachable by any signed-in user: the gallery, every upload endpoint, cluster
    // identification, and the attendee list with its emails and contact details. Nothing
    // caught that, because the failure was silence -- code that was never written rather
    // than code that was wrong.
    //
    // This walks every action in the web assembly and insists that anything taking an
    // eventId has said, out loud, what it requires. Adding a controller without a rule
    // fails here rather than in production.
    public class EventScopedActionGuardTests
    {
        private static bool Declares<T>(MethodInfo action) where T : Attribute =>
            action.GetCustomAttribute<T>(inherit: true) is not null
            || action.DeclaringType!.GetCustomAttribute<T>(inherit: true) is not null;

        private static IEnumerable<MethodInfo> EventScopedActions()
        {
            var controllers = typeof(PhotosController).Assembly
                .GetTypes()
                .Where(t => typeof(Controller).IsAssignableFrom(t) && !t.IsAbstract);

            foreach (var controller in controllers)
            {
                var actions = controller.GetMethods(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

                foreach (var action in actions)
                {
                    if (action.IsSpecialName)
                    {
                        continue;
                    }

                    if (action.GetParameters().Any(p =>
                            string.Equals(p.Name, "eventId", StringComparison.OrdinalIgnoreCase)))
                    {
                        yield return action;
                    }
                }
            }
        }

        [Fact]
        public void Every_event_scoped_action_declares_an_access_rule()
        {
            var undeclared = EventScopedActions()
                .Where(a => !Declares<RequireEventRoleAttribute>(a)
                            && !Declares<NoEventRoleCheckAttribute>(a))
                .Select(a => $"{a.DeclaringType!.Name}.{a.Name}")
                .OrderBy(name => name)
                .ToList();

            Assert.True(
                undeclared.Count == 0,
                "These actions take an eventId but say nothing about who may call them. Add "
                + "[RequireEventRole(...)], or [NoEventRoleCheck(\"reason\")] if the action "
                + "guards itself some other way:\n  " + string.Join("\n  ", undeclared));
        }

        // An opt-out is a judgement call, so it has to carry the judgement with it.
        [Fact]
        public void Every_opt_out_gives_a_reason()
        {
            var unexplained = EventScopedActions()
                .Select(a => new
                {
                    Action = a,
                    OptOut = a.GetCustomAttribute<NoEventRoleCheckAttribute>(inherit: true)
                             ?? a.DeclaringType!.GetCustomAttribute<NoEventRoleCheckAttribute>(inherit: true)
                })
                .Where(x => x.OptOut is not null && string.IsNullOrWhiteSpace(x.OptOut.Reason))
                .Select(x => $"{x.Action.DeclaringType!.Name}.{x.Action.Name}")
                .ToList();

            Assert.True(
                unexplained.Count == 0,
                "NoEventRoleCheck needs a reason: " + string.Join(", ", unexplained));
        }

        // The two consent-critical surfaces, pinned by name. The generic test above would
        // pass if someone widened these to Attendee; these say what the roles must actually
        // be. Identification mints Suggested tags against a real person (D4), and the
        // attendee list is personal data for people who have consented to nothing yet (D3).
        [Fact]
        public void Identification_and_the_attendee_list_stay_staff_only()
        {
            AssertRoles(
                typeof(ClustersController).GetCustomAttribute<RequireEventRoleAttribute>(),
                nameof(ClustersController),
                EventRole.Coordinator, EventRole.Photographer);

            AssertRoles(
                typeof(EventsController).GetMethod(nameof(EventsController.Attendees))!
                    .GetCustomAttribute<RequireEventRoleAttribute>(),
                "EventsController.Attendees",
                EventRole.Coordinator);
        }

        private static void AssertRoles(
            RequireEventRoleAttribute? attribute, string what, params EventRole[] expected)
        {
            Assert.True(attribute is not null, $"{what} has no [RequireEventRole].");
            Assert.Equal(expected.OrderBy(r => r), attribute!.Roles.OrderBy(r => r));
        }
    }
}
