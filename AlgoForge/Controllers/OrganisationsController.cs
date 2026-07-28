using AlgoForge.Data;
using AlgoForge.Models;
using AlgoForge.ViewModels.Organisations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AlgoForge.Controllers
{
    // NOTE: I don't have a full CRUD controller in this repo to copy wholesale --
    // Events only has Create + Index, no Edit/Delete. This follows that file's DI
    // and routing style as closely as possible. There's no org-level authorization
    // policy anywhere in the app yet (RequireEventRole is per-event membership,
    // which doesn't apply here since an Organisation doesn't have EventMemberships
    // of its own), so this is [Authorize]-only, same ceiling as EventsController.Index.
    // Tighten this once a real "who can manage organisations" rule exists.
    [Authorize]
    public class OrganisationsController : Controller
    {
        private readonly AlgoForgeDbContext _db;

        public OrganisationsController(AlgoForgeDbContext db)
        {
            _db = db;
        }

        public async Task<IActionResult> Index()
        {
            var organisations = await _db.Organisations
                .Include(o => o.AdminUser)
                .OrderBy(o => o.Name)
                .ToListAsync();

            return View(organisations);
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            await PopulateUsersAsync();
            return View(new OrganisationFormViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(OrganisationFormViewModel model)
        {
            var duplicate = await _db.Organisations.AnyAsync(o => o.Name == model.Name);
            if (duplicate)
            {
                ModelState.AddModelError(nameof(model.Name), "An organisation with this name already exists.");
            }

            var adminExists = await _db.Users.AnyAsync(u => u.Id == model.AdminUserId);
            if (!adminExists)
            {
                ModelState.AddModelError(nameof(model.AdminUserId), "Select a valid admin user.");
            }

            if (!ModelState.IsValid)
            {
                await PopulateUsersAsync();
                return View(model);
            }

            var organisation = new Organisation
            {
                Id = Guid.NewGuid(),
                Name = model.Name,
                ContactEmail = model.ContactEmail,
                AdminUserId = model.AdminUserId
            };

            _db.Organisations.Add(organisation);
            await _db.SaveChangesAsync();

            TempData["SuccessMessage"] = $"{organisation.Name} created.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Edit(Guid id)
        {
            var organisation = await _db.Organisations.FindAsync(id);
            if (organisation is null) return NotFound();

            var model = new OrganisationFormViewModel
            {
                Id = organisation.Id,
                Name = organisation.Name,
                ContactEmail = organisation.ContactEmail,
                AdminUserId = organisation.AdminUserId
            };

            await PopulateUsersAsync();
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(Guid id, OrganisationFormViewModel model)
        {
            var organisation = await _db.Organisations.FindAsync(id);
            if (organisation is null) return NotFound();

            var duplicate = await _db.Organisations.AnyAsync(o => o.Id != id && o.Name == model.Name);
            if (duplicate)
            {
                ModelState.AddModelError(nameof(model.Name), "An organisation with this name already exists.");
            }

            var adminExists = await _db.Users.AnyAsync(u => u.Id == model.AdminUserId);
            if (!adminExists)
            {
                ModelState.AddModelError(nameof(model.AdminUserId), "Select a valid admin user.");
            }

            if (!ModelState.IsValid)
            {
                model.Id = id;
                await PopulateUsersAsync();
                return View(model);
            }

            // Assign onto the tracked entity rather than _db.Update()-ing a
            // form-built object.
            organisation.Name = model.Name;
            organisation.ContactEmail = model.ContactEmail;
            organisation.AdminUserId = model.AdminUserId;

            await _db.SaveChangesAsync();

            TempData["SuccessMessage"] = $"{organisation.Name} updated.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Delete(Guid id)
        {
            var organisation = await _db.Organisations
                .Include(o => o.AdminUser)
                .FirstOrDefaultAsync(o => o.Id == id);
            if (organisation is null) return NotFound();

            ViewData["EventCount"] = await _db.Events.CountAsync(e => e.OrganisationId == id);
            return View(organisation);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [ActionName(nameof(Delete))]
        public async Task<IActionResult> DeleteConfirmed(Guid id)
        {
            var organisation = await _db.Organisations.FindAsync(id);
            if (organisation is null) return NotFound();

            // Belt-and-braces alongside the Restrict FK: give a clear message instead
            // of letting SaveChangesAsync throw a raw DbUpdateException.
            var hasEvents = await _db.Events.AnyAsync(e => e.OrganisationId == id);
            if (hasEvents)
            {
                TempData["ErrorMessage"] = $"Can't delete {organisation.Name} -- it still has events attached.";
                return RedirectToAction(nameof(Index));
            }

            _db.Organisations.Remove(organisation);
            await _db.SaveChangesAsync();

            TempData["SuccessMessage"] = $"{organisation.Name} deleted.";
            return RedirectToAction(nameof(Index));
        }

        private async Task PopulateUsersAsync()
        {
            ViewData["Users"] = await _db.Users
                .OrderBy(u => u.DisplayName)
                .ToListAsync();
        }
    }
}
