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
        private readonly Services.IEmailSender _emailSender;
        private readonly ILogger<AccountController> _logger;

        public AccountController(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            AlgoForgeDbContext db,
            Services.IEmailSender emailSender,
            ILogger<AccountController> logger)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _db = db;
            _emailSender = emailSender;
            _logger = logger;
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

                // Deliberately not signed in. The address has not been shown to belong to
                // whoever just typed it, and on this product that is not a formality: the
                // whole consent chain -- invitations, claim links, connection requests --
                // is delivered by email, so an unverified address undermines the thing the
                // app exists to guarantee. It also closes the cheapest attack available,
                // which is signing up as staff under somebody else's identity.
                await SendConfirmationEmailAsync(user);

                return RedirectToAction(nameof(ConfirmationSent), new { email = user.Email });
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

            // Checked before the password, so an unconfirmed account cannot be signed into
            // even with the right one. Attendees and delegates arrive already confirmed --
            // opening a link sent to their address is the same proof this is asking for --
            // so in practice this stops staff who registered themselves, which is exactly
            // the population that needs stopping.
            if (!await _userManager.IsEmailConfirmedAsync(user))
            {
                ViewData["UnconfirmedEmail"] = user.Email;
                ModelState.AddModelError(string.Empty,
                    "Confirm your email address before signing in. Check your inbox, and your spam folder.");

                return View(model);
            }

            var result = await _signInManager.PasswordSignInAsync(user, model.Password, model.RememberMe, lockoutOnFailure: true);
            if (!result.Succeeded)
            {
                // Being locked out is worth saying plainly: someone typing their own
                // correct password and still being refused has no way to work out why,
                // and would just keep trying. It does confirm the address is registered,
                // which the generic message below deliberately avoids -- an acceptable
                // trade here, where the alternative is stranding a real user.
                ModelState.AddModelError(
                    string.Empty,
                    result.IsLockedOut
                        ? "This account is temporarily locked after too many failed sign-in attempts. Try again in 15 minutes."
                        : "Invalid login attempt.");

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

        // --- Email confirmation ------------------------------------------------

        [HttpGet]
        public IActionResult ConfirmationSent(string? email)
        {
            ViewData["Email"] = email;
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> ConfirmEmail(string? userId, string? token)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(token))
            {
                return View("ConfirmEmailFailed");
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user is null)
            {
                return View("ConfirmEmailFailed");
            }

            var result = await _userManager.ConfirmEmailAsync(user, token);
            if (!result.Succeeded)
            {
                return View("ConfirmEmailFailed");
            }

            TempData["SuccessMessage"] = "Email confirmed. You can sign in now.";
            return RedirectToAction(nameof(Login));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResendConfirmation(string? email)
        {
            var user = string.IsNullOrWhiteSpace(email)
                ? null
                : await _userManager.FindByEmailAsync(email);

            // Sent only when there is somebody unconfirmed to send it to, but the reply is
            // the same either way -- otherwise this becomes a way to ask which addresses
            // are registered.
            if (user is not null && !await _userManager.IsEmailConfirmedAsync(user))
            {
                await SendConfirmationEmailAsync(user);
            }

            return RedirectToAction(nameof(ConfirmationSent), new { email });
        }

        private async Task SendConfirmationEmailAsync(ApplicationUser user)
        {
            var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);

            var link = Url.Action(
                nameof(ConfirmEmail),
                "Account",
                new { userId = user.Id, token },
                Request.Scheme)!;

            var sent = await _emailSender.SendEmailAsync(
                user.Email!,
                "Confirm your AlgoForge account",
                $"Confirm your email address to finish setting up your account:\n\n{link}\n\n" +
                "If you didn't create this account, you can ignore this message.");

            if (!sent)
            {
                // Worth a log line of its own: the person is now holding an account they
                // cannot sign into, and nothing on their screen distinguishes that from an
                // email that simply has not arrived yet.
                _logger.LogError(
                    "Confirmation email to {Email} could not be sent; that account cannot sign in until it is.",
                    user.Email);
            }
        }

        // --- Forgotten passwords -----------------------------------------------

        [HttpGet]
        public IActionResult ForgotPassword() => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = await _userManager.FindByEmailAsync(model.Email.Trim());

            // Same page whether or not the address is registered. Saying "no such account"
            // would turn this form into a way to test who has one, and on this product the
            // guest list of an event is exactly what must not be answerable.
            //
            // An unconfirmed account is skipped too: letting a reset link double as proof
            // of the address would route straight around the confirmation gate.
            if (user is not null && await _userManager.IsEmailConfirmedAsync(user))
            {
                var token = await _userManager.GeneratePasswordResetTokenAsync(user);

                var link = Url.Action(
                    nameof(ResetPassword),
                    "Account",
                    new { userId = user.Id, token },
                    Request.Scheme)!;

                await _emailSender.SendEmailAsync(
                    user.Email!,
                    "Reset your AlgoForge password",
                    $"Use this link to choose a new password:\n\n{link}\n\n" +
                    "If you didn't ask for this, nothing has changed and you can ignore it.");
            }

            return RedirectToAction(nameof(ForgotPasswordSent));
        }

        [HttpGet]
        public IActionResult ForgotPasswordSent() => View();

        [HttpGet]
        public IActionResult ResetPassword(string? userId, string? token)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(token))
            {
                return View("ResetPasswordFailed");
            }

            return View(new ResetPasswordViewModel { UserId = userId, Token = token });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = await _userManager.FindByIdAsync(model.UserId);
            if (user is null)
            {
                return View("ResetPasswordFailed");
            }

            var result = await _userManager.ResetPasswordAsync(user, model.Token, model.Password);
            if (!result.Succeeded)
            {
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }

                return View(model);
            }

            // Someone who has just proved they hold the address has proved the same thing
            // confirmation asks for, so an account still waiting on that is let through
            // here rather than being told to go and find an older email.
            if (!await _userManager.IsEmailConfirmedAsync(user))
            {
                user.EmailConfirmed = true;
                await _userManager.UpdateAsync(user);
            }

            // A reset is also how somebody locked out gets back in without waiting.
            await _userManager.ResetAccessFailedCountAsync(user);
            await _userManager.SetLockoutEndDateAsync(user, null);

            TempData["SuccessMessage"] = "Password changed. You can sign in now.";
            return RedirectToAction(nameof(Login));
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
