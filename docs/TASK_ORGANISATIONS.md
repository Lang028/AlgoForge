# Task brief: Organisations CRUD

**For:** teammate picking up the Organisations screens
**Work in:** `AlgoForge/AlgoForge/` (the web project)
**Size:** one model change + one migration + one controller + four views

---

## Context — why this exists

Right now AlgoForge **cannot create an organisation at all.** Every event belongs to an
`Organisation`, and the "Create event" page renders an organisation dropdown — but the only
row that ever exists is the single "Demo Org" that `Data/DbInitializer.cs` seeds on startup
for local testing. Adding a second organisation currently means writing SQL by hand.

`DbInitializer` says so in its own comment: *"There's no Organisation/Event creation UI yet
(that's teammate work)."* This is that work.

Once this ships, a coordinator can register a real organisation and create events under it,
which is the first step of the demo flow.

---

## What you are building

| Screen | Route | Who sees it |
|---|---|---|
| Index (list) | `/Organisations` | Any signed-in user |
| Details | `/Organisations/Details/{id}` | Any signed-in user |
| Create | `/Organisations/Create` | Any signed-in user |
| Edit | `/Organisations/Edit/{id}` | **Only that org's admin** |
| Archive / Unarchive | POST only | **Only that org's admin** |

Three decisions already made — build to these, don't re-open them:

1. **Fields:** Name, ContactEmail, Address, PhoneNumber.
2. **Ownership:** whoever creates an organisation becomes its `AdminUser`. Only that user
   can edit or archive it. Everyone signed in can see the list (the event dropdown needs it).
3. **No delete.** Archiving sets a flag. Nothing is ever removed — deleting an organisation
   would cascade toward its events, photos and consent records, which the project's rules
   forbid.

---

## House rules — read this before writing code

This project does **not** follow the default Visual Studio scaffolding patterns. If you
scaffold a controller and ship what it generates, it will be wrong in five ways. Copy the
conventions from `Controllers/EventsController.cs` — that is the closest existing example.

- **Primary keys are `Guid`, never `int`.** Set `Id = Guid.NewGuid()` yourself when creating
  a row. This is a locked project decision (D14 in `BUILD_GUIDE.md`).
- **Target is .NET 8 / EF Core 8.**
- **Use a ViewModel for forms — never `[Bind("...")]` on the entity.** See
  `ViewModels/Events/CreateEventViewModel.cs` for the pattern: a plain class with
  `[Required]`, `[Display(Name = "...")]` etc., mapped by hand in the controller.
- **Never put a navigation property in a form post.** No `Organisation.Events`, no
  `AdminUser` — the form carries only the four editable fields.
- **The DbContext field is called `_db`,** not `_context`.
- **Everything is async:** `ToListAsync()`, `FirstOrDefaultAsync()`, `SaveChangesAsync()`.
- **`[Authorize]` on the controller class, `[ValidateAntiForgeryToken]` on every POST.**
- **Messages go through `TempData["SuccessMessage"]` / `TempData["ErrorMessage"]`** — the
  existing views already render these.
- **UI:** Bootstrap 5, plain tables and forms, no emoji, no decorative styling. Match
  `Views/Events/Index.cshtml` and `Views/Events/Create.cshtml` visually — copy their
  structure and change the fields.

---

## Step 1 — Model + migration

Edit `Models/Organisation.cs`. Keep the existing members; add three:

```csharp
[Required, MaxLength(200)]
public string Address { get; set; } = string.Empty;

[Required, MaxLength(30)]
public string PhoneNumber { get; set; } = string.Empty;

// Archived organisations stay in the database but are hidden from the
// event-creation dropdown. We never hard-delete: an organisation owns events,
// which own photos, which own consent records.
public bool IsArchived { get; set; } = false;
```

Leave `AdminUserId` and the `AdminUser` navigation exactly as they are — the relationship is
already configured in `Data/AlgoForgeDbContext.cs` and needs no change.

Then, from the repo root:

```bash
dotnet ef migrations add AddOrganisationContactDetails --project AlgoForge/AlgoForge
```

```bash
dotnet ef database update --project AlgoForge/AlgoForge
```

Check the generated migration before running it: it should contain three `AddColumn` calls
and nothing else. If it wants to drop or recreate tables, stop and ask — that means your
local database is out of step with the migration history.

---

## Step 2 — ViewModel

New file `ViewModels/Organisations/OrganisationFormViewModel.cs`, namespace
`AlgoForge.ViewModels.Organisations`. One view model serves both Create and Edit:

- `Guid Id` — empty on create, populated on edit
- `string Name` — `[Required]`, `[MaxLength(200)]`, `[Display(Name = "Organisation name")]`
- `string ContactEmail` — `[Required]`, `[EmailAddress]`, `[Display(Name = "Contact email")]`
- `string Address` — `[Required]`, `[MaxLength(200)]`
- `string PhoneNumber` — `[Required]`, `[Phone]`, `[Display(Name = "Phone number")]`

---

## Step 3 — Controller

New file `Controllers/OrganisationsController.cs`. Constructor-inject `AlgoForgeDbContext _db`
and `UserManager<ApplicationUser> _userManager`.

**Getting the current user id.** Copy this helper from `TagsController` / `ConnectionsController` —
`GetUserId` returns a string, and every id in this project is a `Guid`:

```csharp
private Guid? CurrentUserId()
{
    var userIdText = _userManager.GetUserId(User);
    return userIdText is not null && Guid.TryParse(userIdText, out var userId) ? userId : null;
}
```

Return `Challenge()` when it comes back null.

**Actions:**

- `Index()` — list all organisations ordered by name. Show archived ones too, visually marked
  (a muted badge), so an admin can find and unarchive them. Include an event count per row
  via `.Include(o => o.Events)` or a projection.
- `Details(Guid id)` — one organisation plus its events. `NotFound()` if it doesn't exist.
- `Create()` GET — return an empty form.
- `Create(OrganisationFormViewModel model)` POST — on invalid `ModelState`, re-render the form.
  Otherwise build the entity with `Id = Guid.NewGuid()` and
  **`AdminUserId = CurrentUserId().Value`**, save, set `TempData["SuccessMessage"]`, redirect
  to `Index`.
- `Edit(Guid id)` GET — load it, **check `organisation.AdminUserId == CurrentUserId()`**, and
  return `Forbid()` if not. Map entity → view model.
- `Edit(Guid id, OrganisationFormViewModel model)` POST — same ownership check again (never
  trust that the GET ran), then copy the four fields onto the tracked entity and save. Do
  **not** call `_db.Update(entity)` with an object built from the form — load the real row
  first and assign fields onto it, so `AdminUserId` and `IsArchived` can't be overwritten by
  a crafted post.
- `Archive(Guid id)` POST and `Unarchive(Guid id)` POST — ownership check, flip `IsArchived`,
  save, redirect to `Index` with a message.

The ownership check is a real check, not a UI detail: hiding the Edit button is not enough,
because anyone can type the URL.

---

## Step 4 — Views

Four files under `Views/Organisations/`: `Index.cshtml`, `Details.cshtml`, `Create.cshtml`,
`Edit.cshtml`.

- Start from `Views/Events/Index.cshtml` and `Views/Events/Create.cshtml` and change the
  fields — same table markup, same `asp-for` / `asp-validation-for` form structure, same
  `TempData["SuccessMessage"]` alert block at the top.
- Create and Edit both need the validation scripts section at the bottom:

  ```cshtml
  @section Scripts {
      @{await Html.RenderPartialAsync("_ValidationScriptsPartial");}
  }
  ```

- On `Index`, only render Edit / Archive controls for rows where the current user is the
  admin. Archive and Unarchive are **forms with POST**, not links — a GET must never change
  data.
- On `Details`, list the organisation's events with a link through to each event's gallery.

---

## Step 5 — Wire it up

Two small edits outside your new files:

1. **`Views/Shared/_Layout.cshtml`** — add an "Organisations" nav item next to the existing
   "Events" link, inside the `@if (User.Identity?.IsAuthenticated ?? false)` block.

2. **`Controllers/EventsController.cs`** — the organisation dropdown must not offer archived
   organisations. There are **two** places loading it (the `Create` GET and the invalid-model
   path of the `Create` POST); both currently read:

   ```csharp
   await _db.Organisations.OrderBy(o => o.Name).ToListAsync()
   ```

   Add `.Where(o => !o.IsArchived)` before the `OrderBy` in both.

---

## Definition of done

Run the app and walk through this end to end:

1. Sign in. "Organisations" appears in the nav.
2. Create an organisation with all four fields. It appears in the list, and you are its admin.
3. Try to save with a blank name and a malformed email — both show validation errors and
   nothing is saved.
4. Edit your organisation; the changes persist.
5. Go to **Events → Create event**. Your new organisation appears in the dropdown. Create an
   event under it.
6. Archive the organisation. It shows as archived in the list and **disappears from the event
   dropdown**. Unarchive it; it comes back.
7. Sign in as a different user (register a second account). You can see the organisation in
   the list, but there is no Edit button — and pasting the `/Organisations/Edit/{id}` URL
   directly gives a 403, not an edit form.

Step 7 is the one people skip. Please don't.

---

## Things to avoid

- **Don't copy controllers from the `Forge` repo.** Same domain, but `int` keys, a different
  .NET version, and a different Identity setup — nothing transfers cleanly, and its
  controllers have no authorization or ownership checks at all.
- **Don't add a Delete action.** Archive is the decision.
- **Don't touch** `Data/AlgoForgeDbContext.cs`, any existing migration, or anything under
  `Services/PersonPipeline/`, `Controllers/TagsController.cs`, `Controllers/ClustersController.cs`,
  or `face_service/`. Those carry the consent rules and are not part of this task.
- **Don't hand-edit a generated migration file.** If it looks wrong, delete it, fix the model,
  and regenerate.

---

## Note for Lange (not part of the brief)

The ownership check specified here is a local, hand-written check inside one controller. It is
not the policy-based per-event authorization the build guide locks in as D2 — that still
doesn't exist anywhere in the app. This brief deliberately doesn't try to build D2, but the
check it does add is written in a form that a later `[Authorize(Policy = ...)]` pass can
replace cleanly.
