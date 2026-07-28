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
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = new ApplicationUser
            {
                UserName = model.Email,
                Email = model.Email,
                DisplayName = model.DisplayName
            };

            var result = await _userManager.CreateAsync(user, model.Password);
            if (result.Succeeded)
            {
                // No membership is granted here. A new account has standing in no event
                // until it creates one, accepts an invitation, or claims an attendee record.
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

            // Persist the chosen role as an "ActiveRole" claim and refresh the cookie so
            // the whole app can read it. It is a lens, not a permission: per-event access
            // still comes from EventMembership via RequireEventRole.
            var role = model.SelectedRole!.Value;
            var existingClaim = (await _userManager.GetClaimsAsync(user))
                .FirstOrDefault(c => c.Type == "ActiveRole");
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
