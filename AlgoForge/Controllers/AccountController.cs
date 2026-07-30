using AlgoForge.Data;
using AlgoForge.Models;
using AlgoForge.ViewModels.Account;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AlgoForge.Controllers
{
    public class AccountController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly AlgoForgeDbContext _db;

        public AccountController(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager, AlgoForgeDbContext db)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _db = db;
        }

        [HttpGet]
        public IActionResult Register(string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterViewModel model, string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;

            // Only the two staff roles may be chosen here. Attendee and Delegate arrive by
            // invite link, and Admin is never self-assigned -- a posted value outside this
            // pair is rejected rather than trusted.
            if (model.SignUpAs is not (EventRole.Coordinator or EventRole.Photographer))
            {
                ModelState.AddModelError(nameof(model.SignUpAs),
                    "Choose whether you're signing up as an organisation or a photographer.");
            }

            // An organisation is named by its organisation name; a photographer by their own.
            var signingUpAsOrganisation = model.SignUpAs == EventRole.Coordinator;

            if (signingUpAsOrganisation && string.IsNullOrWhiteSpace(model.OrganisationName))
            {
                ModelState.AddModelError(nameof(model.OrganisationName), "Enter your organisation's name.");
            }

            if (!signingUpAsOrganisation && string.IsNullOrWhiteSpace(model.DisplayName))
            {
                ModelState.AddModelError(nameof(model.DisplayName), "Enter your full name.");
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = new ApplicationUser
            {
                UserName = model.Email,
                Email = model.Email,
                // The organisation's name is what shows on its events, so it is carried
                // through as the display name rather than asking for a personal one too.
                DisplayName = signingUpAsOrganisation
                    ? model.OrganisationName!.Trim()
                    : model.DisplayName!.Trim()
            };

            var result = await _userManager.CreateAsync(user, model.Password);
            if (result.Succeeded)
            {
                // Still no membership. The choice above is recorded as the ActiveRole claim
                // only -- a lens for the dashboard and navigation. Standing in an event
                // continues to come from creating one, accepting an invitation, or claiming
                // an attendee record.
                await _userManager.AddClaimAsync(user,
                    new System.Security.Claims.Claim("ActiveRole", model.SignUpAs!.Value.ToString()));

                // An organisation account gets its Organisation straight away, with the
                // registrant as its admin -- otherwise they'd sign up as an organisation and
                // then have nowhere to put an event. This is the one place an organisation
                // is created by the person it belongs to; a photographer handing an event
                // over still invites by email rather than creating one on someone's behalf.
                if (signingUpAsOrganisation)
                {
                    _db.Organisations.Add(new Organisation
                    {
                        Id = Guid.NewGuid(),
                        Name = model.OrganisationName!.Trim(),
                        ContactEmail = string.IsNullOrWhiteSpace(model.OrganisationContactEmail)
                            ? model.Email.Trim()
                            : model.OrganisationContactEmail.Trim(),
                        AdminUserId = user.Id
                    });

                    await _db.SaveChangesAsync();
                }

                await _signInManager.SignInAsync(user, isPersistent: false);
                return RedirectToLocal(returnUrl);
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return View(model);
        }

        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            // The user is resolved up front (rather than reading User after sign-in,
            // which still holds the anonymous principal for this request) so the role
            // claim and membership check below run against the right account.
            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user is null)
            {
                ModelState.AddModelError(string.Empty, "Invalid login attempt.");
                return View(model);
            }

            var result = await _signInManager.PasswordSignInAsync(user, model.Password, model.RememberMe, lockoutOnFailure: false);
            if (!result.Succeeded)
            {
                ModelState.AddModelError(string.Empty, "Invalid login attempt.");
                return View(model);
            }

            // A platform admin who signs in here goes straight to the admin console --
            // the role dropdown and ActiveRole claim are member concepts.
            if (await _userManager.IsInRoleAsync(user, Data.DbInitializer.SystemAdminRole))
            {
                return RedirectToAction(nameof(AdminController.Index), "Admin");
            }

            // Signing in no longer asks which role you want. The role chosen at sign-up is
            // remembered, and if it doesn't match anything the account actually holds --
            // someone who signed up as a photographer and has since claimed an attendee
            // invite -- it falls back to a role they really do have.
            var existingClaim = (await _userManager.GetClaimsAsync(user))
                .FirstOrDefault(c => c.Type == "ActiveRole");

            var role = await ResolveActiveRoleAsync(user, model.SelectedRole, existingClaim?.Value);

            var newClaim = new System.Security.Claims.Claim("ActiveRole", role.ToString());
            if (existingClaim is null)
            {
                await _userManager.AddClaimAsync(user, newClaim);
            }
            else if (existingClaim.Value != newClaim.Value)
            {
                await _userManager.ReplaceClaimAsync(user, existingClaim, newClaim);
            }
            await _signInManager.RefreshSignInAsync(user);

            // Explicit returnUrl always wins over the role dropdown -- e.g. someone
            // following a claim link shouldn't get bounced to a role dashboard instead.
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            // Signing in with a role you hold on no event is allowed -- a new coordinator
            // has to be able to get in to create their first event -- but the dashboard
            // explains how to actually get that role. Admin is backed by
            // Organisation.AdminUserId rather than EventMembership.
            var hasRole = role == EventRole.Admin
                ? await _db.Organisations.AnyAsync(o => o.AdminUserId == user.Id)
                : await _db.EventMemberships.AnyAsync(m => m.UserId == user.Id && m.Role == role);
            if (!hasRole)
            {
                TempData["InfoMessage"] = role switch
                {
                    EventRole.Admin =>
                        "You don't administer any organisation yet. Create one to get started.",
                    EventRole.Coordinator =>
                        "You don't have a Coordinator role on any event yet. Create your first event to get started.",
                    EventRole.Photographer =>
                        "You don't have a Photographer role on any event yet. Create an event, or ask a coordinator to add you to theirs.",
                    _ =>
                        $"You don't have a {role} role on any event yet. Open the invitation link from your email to claim your attendee record."
                };
            }

            return RedirectToAction(nameof(HomeController.Index), "Home");
        }

        // Which role the app should present this account with.
        //
        // Preference order: anything explicitly asked for, then the role remembered from
        // sign-up, then whatever the account actually holds. The remembered role is only
        // kept if it still makes sense -- an account that signed up as a photographer and
        // has since only ever claimed attendee invites should land on the attendee side.
        private async Task<EventRole> ResolveActiveRoleAsync(
            ApplicationUser user, EventRole? requested, string? rememberedValue)
        {
            if (requested is { } explicitChoice)
            {
                return explicitChoice;
            }

            var held = await _db.EventMemberships
                .Where(m => m.UserId == user.Id)
                .Select(m => m.Role)
                .Distinct()
                .ToListAsync();

            if (await _db.Organisations.AnyAsync(o => o.AdminUserId == user.Id))
            {
                held.Add(EventRole.Admin);
            }

            if (Enum.TryParse<EventRole>(rememberedValue, out var remembered))
            {
                // Staff roles are kept even with nothing to show yet: a new coordinator has
                // to be able to get in and create their first event.
                if (held.Contains(remembered)
                    || remembered is EventRole.Coordinator or EventRole.Photographer)
                {
                    return remembered;
                }
            }

            // Nothing remembered that still applies -- fall back to what they actually hold,
            // most capable first.
            foreach (var candidate in new[]
                     {
                         EventRole.Admin, EventRole.Coordinator, EventRole.Photographer,
                         EventRole.Attendee, EventRole.Delegate
                     })
            {
                if (held.Contains(candidate))
                {
                    return candidate;
                }
            }

            return EventRole.Attendee;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            return RedirectToAction(nameof(HomeController.Index), "Home");
        }

        [HttpGet]
        public IActionResult AccessDenied()
        {
            return View();
        }

        private IActionResult RedirectToLocal(string? returnUrl)
        {
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }
            return RedirectToAction(nameof(HomeController.Index), "Home");
        }
    }
}
