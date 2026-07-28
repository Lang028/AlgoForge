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

            var result = await _signInManager.PasswordSignInAsync(model.Email, model.Password, model.RememberMe, lockoutOnFailure: false);
            if (!result.Succeeded)
            {
                ModelState.AddModelError(string.Empty, "Invalid login attempt.");
                return View(model);
            }

            // Explicit returnUrl always wins over the role dropdown -- e.g. someone
            // following a claim link shouldn't get bounced to a role dashboard instead.
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            if (model.SelectedRole is { } role)
            {
                var userIdText = _userManager.GetUserId(User);
                if (userIdText is not null && Guid.TryParse(userIdText, out var userId))
                {
                    var hasRole = await _db.EventMemberships
                        .AnyAsync(m => m.UserId == userId && m.Role == role);

                    if (!hasRole)
                    {
                        TempData["ErrorMessage"] = $"You don't have a {role} role on any event yet.";
                        return RedirectToAction(nameof(HomeController.Index), "Home");
                    }

                    return role switch
                    {
                        EventRole.Coordinator => RedirectToAction(nameof(EventsController.Index), "Events"),
                        EventRole.Photographer => RedirectToAction(nameof(PhotosController.Index), "Photos"),
                        EventRole.Attendee => RedirectToAction(nameof(ConnectionsController.Index), "Connections"),
                        EventRole.Delegate => RedirectToAction(nameof(EventsController.Index), "Events"),
                        _ => RedirectToAction(nameof(HomeController.Index), "Home")
                    };
                }
            }

            return RedirectToLocal(returnUrl);
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
