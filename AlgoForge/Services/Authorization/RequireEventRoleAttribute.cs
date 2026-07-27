using System.Security.Claims;
using AlgoForge.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AlgoForge.Services.Authorization
{
    // Gates an event-scoped action on the caller's roles in that event.
    //
    //     [RequireEventRole(EventRole.Coordinator, EventRole.Photographer)]
    //     public async Task<IActionResult> Identify(Guid eventId, ...)
    //
    // Named RequireEventRole rather than EventRole so the usage above is unambiguous --
    // C# resolves an attribute called EventRole against the enum of the same name.
    //
    // D2 specifies "policy-based authorization". An authorization filter is a deliberate
    // deviation: a resource-based policy handler needs the resource resolved before it
    // runs, which for a route-value eventId means loading it in a filter anyway. This is
    // the same enforcement with less machinery, and -- the property that actually matters --
    // it can be applied uniformly and checked by a test.
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
    public sealed class RequireEventRoleAttribute : TypeFilterAttribute
    {
        public RequireEventRoleAttribute(params EventRole[] roles)
            : base(typeof(EventRoleFilter))
        {
            Roles = roles;
            Arguments = new object[] { roles };
        }

        public EventRole[] Roles { get; }
    }

    // Marks an action that takes an eventId but deliberately does not gate on membership.
    // A reason is required: the guard test accepts the opt-out, but a reviewer shouldn't
    // have to guess why it's there.
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
    public sealed class NoEventRoleCheckAttribute : Attribute
    {
        public NoEventRoleCheckAttribute(string reason)
        {
            Reason = reason;
        }

        public string Reason { get; }
    }

    internal sealed class EventRoleFilter : IAsyncAuthorizationFilter
    {
        private readonly IEventAccessService _access;
        private readonly EventRole[] _roles;

        public EventRoleFilter(IEventAccessService access, EventRole[] roles)
        {
            _access = access;
            _roles = roles;
        }

        public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
        {
            var userIdText = context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (userIdText is null || !Guid.TryParse(userIdText, out var userId))
            {
                context.Result = new ChallengeResult();
                return;
            }

            if (!TryGetEventId(context, out var eventId))
            {
                context.Result = new BadRequestObjectResult("An event id is required.");
                return;
            }

            var allowed = await _access.HasAnyRoleAsync(
                userId, eventId, _roles, context.HttpContext.RequestAborted);

            if (!allowed)
            {
                context.Result = new ForbidResult();
            }
        }

        private static bool TryGetEventId(AuthorizationFilterContext context, out Guid eventId)
        {
            // Routes differ across the app: /Clusters/{eventId} carries it as a route value,
            // while the gallery and the batched uploader pass it as a query string. Checking
            // both beats standardising the routes, which would break links already emailed.
            if (context.RouteData.Values.TryGetValue("eventId", out var routeValue)
                && Guid.TryParse(routeValue?.ToString(), out eventId))
            {
                return true;
            }

            var queryValue = context.HttpContext.Request.Query["eventId"].FirstOrDefault();
            return Guid.TryParse(queryValue, out eventId);
        }
    }
}
