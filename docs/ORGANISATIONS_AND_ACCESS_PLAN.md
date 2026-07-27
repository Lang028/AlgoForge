# Organisations, photographers and access control — design plan

**Status:** agreed in principle (Lange, 27 July 2026), not yet built.
**Supersedes:** `docs/TASK_ORGANISATIONS.md` in part — see §8.
**Amends:** `BUILD_GUIDE.md` §1 and §7 — see §7 of this document.

---

## 1. What this is, and what it reopens

The product needs two entry points, not one:

- A **photographer** signs up, creates an event, uploads photos, and invites an **event
  organiser** so the event can sit under an **organisation** and be managed by them.
- For a small shoot, that same photographer runs the whole event alone and never involves
  an organisation at all.

And the relationship between the two sides is many-to-many: **an organisation has many
photographers across many events, and a photographer works for many organisations.**

`BUILD_GUIDE.md` §1 currently records "the photographer is the entry point — LOCKED", with
organisation-initiated events explicitly deferred in §7. This plan builds both directions,
so that decision is being **amended by the owner**, not worked around. §7 below lists the
register changes that must land in `BUILD_GUIDE.md` for the decision to count (working
rule 3: a decision that isn't recorded there doesn't exist).

---

## 2. Where the code actually stands

This matters more than it looks, because it changes the character of the work.

**`EventMembership` is written in two places and read in none.** `EventsController.Create`
creates one, `AccountController.Register` creates one, and no code anywhere consults it to
decide whether an action is allowed. D2's permission spine exists as a table and as an idea,
but not as an enforcement mechanism.

Two consequences follow:

**Registration hands out staff roles on request.** `Views/Account/Register.cshtml` renders a
dropdown of every event in the database and a role selector defaulting to Photographer
(`ViewModels/Account/RegisterViewModel.cs:38`). Anyone who registers picks an event and a
role and is granted it. Nothing validates the choice.

**The staff-facing controllers have no access checks at all.** The split is clean and
explains itself:

| Controller | Guarded? | Why |
|---|---|---|
| `TagsController` | Yes | Checks `Attendee.ClaimedByUserId == currentUser` before confirm/reject. The consent invariant is correctly enforced. |
| `ConnectionsController` | Yes | Scopes to the viewer, `Forbid()`s on mismatch, strips contact fields server-side. |
| `PhotosController` | **No** | `Index`, `Upload`, `UploadBatch`, `FinishUpload` take an `eventId` and check nothing. Any signed-in user can browse or upload to any event. |
| `ClustersController` | **No** | All four actions take an `eventId` and check nothing. Any signed-in user can **identify people** in any event — which mints Suggested tags against real attendees. |
| `EventsController` | **No** | `Index` lists every event to everyone. `Attendees`, `ImportAttendees` and `SendInvite` expose and mutate the invitee list — names, emails, contact info. |

The attendee-facing half is safe because attendee identity is checked against a row the user
demonstrably owns (`Attendee.ClaimedByUserId`). The staff-facing half is unguarded because
staff identity has no equivalent check — `EventMembership` was never read.

So this is not a matter of retrofitting a new permission model over a working one. There is
no incumbent model to break. That is the one piece of good luck here.

---

## 3. The core idea: three concepts, kept apart

Most of the difficulty in this design disappears once these three stop being the same thing.

| Concept | Carried by | Answers |
|---|---|---|
| **Affiliation** | `Event.OrganisationId` (becoming nullable) | Whose event is this, for branding, grouping and reporting? |
| **Event authorization** | `EventMembership` | What may this user do *inside* this event? |
| **Organisation authorization** | `OrganisationMembership` (new) | What may this user do *to the organisation* — create events under it, manage its roster? |

The many-to-many the product needs falls straight out of this and needs no special
machinery: a photographer has `OrganisationMembership` rows at several organisations and
`EventMembership` rows at many events; an organisation has many of both. Nothing is
single-valued, so nothing needs to be widened later.

**An independent event has `OrganisationId = null`.** It is not a placeholder or a phantom
organisation — it genuinely has no organisation until one adopts it. Adoption is a single
foreign-key update plus membership grants, done in one transaction. This works precisely
because no permission logic reads `OrganisationId` directly; only the access resolver in
§4 does, in one place.

---

## 4. Permission resolution

### 4.1 A user can hold more than one role on an event

This falls out of the solo-photographer requirement and it contradicts an assumption
(not a decision) in the current code, so it is worth stating plainly.

D1 locks Coordinator and Photographer as **disjoint** roles: a Coordinator cannot upload
photos. But a photographer running a small event alone must do both — configure the event
*and* upload to it. The resolution is not to weaken D1 or invent a fifth role. It is to let
one user hold **two membership rows** on the same event, one per role. The roles stay
disjoint; the person holds both.

`EventMembership` already permits this — there is no unique constraint on
`(EventId, UserId)`. Add one on `(EventId, UserId, Role)` so the same role can't be granted
twice, and change the resolver to return a **set** of roles rather than a single role.

### 4.2 The resolver

One service, and it is the only thing in the codebase allowed to answer "what may this user
do in this event":

```csharp
public interface IEventAccessService
{
    Task<IReadOnlySet<EventRole>> ResolveRolesAsync(Guid userId, Guid eventId);
    Task<bool> HasAnyRoleAsync(Guid userId, Guid eventId, params EventRole[] roles);
}
```

Resolution, in order, accumulating:

1. Every `EventMembership` row for `(userId, eventId)` contributes its role.
2. **If** the event has an `OrganisationId` **and** the user holds
   `OrganisationMembership(Owner | Manager)` on that organisation, add `EventRole.Coordinator`.
3. Empty set means no access — the action returns 403.

Step 2 is the direct implementation of the decision that org Owners and Managers get full
access to their organisation's events. It is deliberately expressed as *"the org role
resolves to an effective Coordinator membership"* rather than as a second, parallel
permission path. There is still exactly one function answering the access question; it just
has a two-clause definition inside it, in one file, with tests.

Note what step 2 does **not** do: roster **Photographers** get nothing automatic. A
freelancer added to an organisation's roster for one wedding must not thereby see the
organisation's other forty events. They are granted access per event, explicitly.

### 4.3 Enforcement

An authorization filter attribute, applied to every event-scoped action:

```csharp
[EventRole(EventRole.Coordinator, EventRole.Photographer)]
public async Task<IActionResult> Upload(Guid eventId, List<IFormFile>? files)
```

The filter reads `eventId` from the route or query string, calls `IEventAccessService`, and
short-circuits with 403 when the intersection is empty.

D2's wording specifies "policy-based authorization". An `IAsyncAuthorizationFilter`
attribute is a deliberate, and honest, deviation: resource-based policy handlers need the
resource resolved before the handler runs, which for a route-value `eventId` means loading
it in a filter anyway. The attribute form is the same enforcement with less machinery, and
it can be applied uniformly — which is the property that actually matters here.

**The guard test is part of this work, not a nice-to-have.** A test reflects over every
controller action, finds the ones with an `eventId` parameter, and asserts each carries
either `[EventRole]` or an explicit `[NoEventRoleCheck]` opt-out. That is what stops this
hole reopening the next time someone adds a controller, and it costs about thirty lines.

### 4.4 Contact visibility is unchanged

Nothing here touches the consent rules. Confirmed-tags-only labelling (D20), contact
stripping (D11), and the `IsTaggable` gate (D21) all stay exactly as they are. An org
Manager resolving to Coordinator gets what a Coordinator gets — including the attendee list
with contact details, which coordinators already have by construction under D3, since they
uploaded it. So this decision does not create a new category of exposure; it grants an
existing one to the organisation persona that was always meant to have it.

---

## 5. Entities

### 5.1 New

```csharp
public enum OrganisationRole
{
    Owner,        // created or was transferred the org; can transfer ownership and archive
    Manager,      // the "event organiser": creates events under the org, manages the roster
    Photographer  // on the roster; assignable to the org's events; no automatic event access
}

public class OrganisationMembership
{
    public Guid Id { get; set; }
    public Guid OrganisationId { get; set; }
    public Organisation? Organisation { get; set; }
    public Guid UserId { get; set; }
    public ApplicationUser? User { get; set; }
    public OrganisationRole Role { get; set; }
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
}
```

Unique index on `(OrganisationId, UserId)` — one role per person per organisation, unlike
events. Owner implies everything Manager can do; the roles are ranked, not disjoint.

```csharp
public enum InvitationScope { Event, Organisation }
public enum InvitationStatus { Pending, Accepted, Declined, Revoked, Expired }

public class Invitation
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;   // normalised lowercase

    public InvitationScope Scope { get; set; }
    public Guid? EventId { get; set; }
    public Guid? OrganisationId { get; set; }

    public EventRole? EventRole { get; set; }
    public OrganisationRole? OrganisationRole { get; set; }

    // Event scope only. The handover flag: on acceptance the invitee is offered the chance
    // to attach this event to an organisation they own or manage.
    public bool AllowsOrganisationAdoption { get; set; }

    public string Token { get; set; } = string.Empty;
    public InvitationStatus Status { get; set; } = InvitationStatus.Pending;
    public DateTime ExpiresAt { get; set; }

    public Guid InvitedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RespondedAt { get; set; }
}
```

Exactly one of `EventId` / `OrganisationId` is set, with the matching role field. Enforced in
the service layer and asserted in tests; a database check constraint is optional polish.

**This is separate from the attendee invite and must stay separate.** `Attendee.InviteToken`
(D3) invites a *person who has no account* to claim a pre-existing person record. An
`Invitation` grants a *role* to an account holder. Merging them would collapse the claim
model that makes consent structurally enforceable.

### 5.2 Changed

**`Event`**
- `OrganisationId` becomes `Guid?`.
- Add `CreatedByUserId` (`Guid`) — who stood the event up. Needed by the permanence rule
  below and by "my events".
- Add `CreatedAt` (`DateTime`).

**`EventMembership`**
- Add `IsPermanent` (`bool`, default `false`). Set on the memberships granted to whoever
  creates an event. The remove-member action refuses to delete a row with this set.

  This is the decision that a photographer's access survives handover and cannot be revoked
  by the organisation. It needs to be a column rather than a convention, because it is the
  thing that makes handover safe to do at all: without it, handing an event to a client
  means asking a photographer to risk being locked out of their own work.

**`Organisation`**
- `AdminUserId` is replaced by `OrganisationMembership(Owner)`. Migrate in two steps: the
  first migration adds the membership table and backfills an Owner row from each existing
  `AdminUserId`; nothing reads `AdminUserId` after that; a later migration drops the column.
  Dropping it in the same migration that backfills from it is how you lose the data if the
  backfill is wrong.
- Add `Address`, `PhoneNumber`, `IsArchived` as specified in `docs/TASK_ORGANISATIONS.md`
  §Step 1 — that part of the brief is unaffected by this plan.

---

## 6. The flows

### A. Photographer runs a small event alone

1. Registers. A plain account — no event dropdown, no role selector.
2. Creates an event. The organisation field offers *"No organisation — I'm running this
   myself"* alongside any organisations they belong to.
3. Gets two memberships on it, `Coordinator` and `Photographer`, both `IsPermanent = true`.
4. Uploads photos, imports the invitee list, identifies clusters, sends claim links. The
   whole existing flow, unchanged, now correctly scoped to them.

### B. Photographer hands the event to an organiser — the headline flow

1. Photographer has an independent event with photos already in it.
2. **Invite an event organiser**: enters an email, optionally a message, and a checkbox
   *"Let them attach this event to their organisation"* (default on). Creates an
   `Invitation` — `Scope = Event`, `EventRole = Coordinator`,
   `AllowsOrganisationAdoption = true`, tokenized, 14-day expiry — and emails the link (D12).
3. Organiser opens the link. If they have no account they register first; the token, not the
   email address, is what authorises acceptance, so registering with a different address
   still works.
4. The link lands on a **confirmation page, and acceptance is a POST.** Same reasoning as
   the connection flow in D20: a mail client prefetching links must not be able to accept on
   the recipient's behalf. The page names the inviter, the event, and the role being granted.
5. On accept: `EventMembership(organiser, event, Coordinator)`; invitation → `Accepted`.
6. Because adoption was allowed, the next screen offers **Attach to an organisation** — pick
   one where they are Owner or Manager, or create a new one on the spot and become its Owner.
   On confirm, `Event.OrganisationId` is set.
7. The photographer keeps both memberships. The organisation's roster screen shows them as
   *"Photographer (original) — cannot be removed."*

**Adoption only ever happens this way, or by the event's own Coordinator choosing an
organisation they belong to. An organisation can never pull an event in.** This rule is
load-bearing given that org Owners and Managers resolve to Coordinator: without it, an org
Manager could adopt any event by id and instantly read its attendee contact list.

### C. Organisation creates the event and invites photographers

1. User creates an organisation and becomes its `Owner`.
2. Creates events under it. No explicit `EventMembership` is needed — the Owner already
   resolves to Coordinator on every event the organisation holds (§4.2 step 2). One less
   row to keep consistent.
3. Invites a photographer, either onto the **roster**
   (`Scope = Organisation, OrganisationRole = Photographer`) or straight onto a single
   **event** (`Scope = Event, EventRole = Photographer`).
4. Assigning someone already on the roster to an event is a direct `EventMembership` grant —
   no email, no token. They already accepted the organisation.

### D. Roster management

`/Organisations/{id}/Members` — list, invite, change role, remove. Rules:

- An organisation always has at least one Owner; the last one cannot be removed or demoted.
- Removing someone from the roster does **not** revoke their existing `EventMembership`
  rows. Those were granted per event and are ended per event. Silent mass-revocation is
  exactly the kind of thing that loses a photographer their access to a finished job.
- Detaching an event from an organisation requires an org Owner, or the event's permanent
  creator.

---

## 7. Amendments needed in `BUILD_GUIDE.md`

Per working rule 3, these must be written into the register or they do not exist. Proposed
numbering continues from D21.

- **§1 core flow** — amend "the photographer is the entry point — LOCKED" to record that
  both entry points are now supported, and remove the deferral note.
- **§7 Deferred** — remove "Organisation-initiated events"; it is being built.
- **D22. Affiliation is not authorization.** `Event.OrganisationId` becomes nullable and
  carries affiliation only. Access is decided by `EventMembership` via a single resolver.
- **D23. Organisation membership.** `OrganisationMembership(user, org, role)` with
  Owner/Manager/Photographer. Replaces `Organisation.AdminUserId`. Gives the org↔photographer
  many-to-many.
- **D24. Org Owners and Managers resolve to Coordinator on their organisation's events.**
  Roster Photographers get no automatic event access. Adoption of an event into an
  organisation is consent-based and can never be initiated by the organisation.
- **D25. A user may hold multiple roles on one event.** Resolves the collision between D1's
  disjoint Coordinator/Photographer roles and a solo photographer needing both. Roles stay
  disjoint; people can hold two.
- **D26. The original photographer's event access is permanent.** `EventMembership.IsPermanent`;
  an organisation cannot revoke it after handover.
- **D27. `Invitation` grants roles; `Attendee.InviteToken` claims person records.** Two
  mechanisms, never merged.

---

## 8. Effect on `docs/TASK_ORGANISATIONS.md`

That brief is roughly 80% still correct and should be **revised before the teammate starts**,
not discarded. What changes:

- **Step 1 (model + migration)** — keep exactly as written. `Address`, `PhoneNumber`,
  `IsArchived` are all still wanted.
- **Ownership (decision 2 in the brief)** — changes. "Whoever creates an organisation becomes
  its `AdminUser`, and only that user can edit" becomes "becomes its `Owner` via
  `OrganisationMembership`, and any Owner or Manager can edit". The brief's instruction to
  leave `AdminUserId` alone is now wrong.
- **Step 3 (controller)** — the hand-written `organisation.AdminUserId == CurrentUserId()`
  check becomes a roster lookup. The brief's closing note already anticipates this: it says
  the check is written "in a form that a later policy pass can replace cleanly". This is that
  pass.
- **New screen** — `Members` (list, invite, change role, remove) is not in the brief and needs
  adding.
- **No delete** — unchanged, still right.

---

## 9. Build order

Phased so that stopping early still leaves something coherent. Phase 3 is the largest and
sits closest to the demo-critical path.

**Phase 0 — close the access holes. BUILT, 27 July 2026.** No schema change.
- `Services/Authorization/` — `IEventAccessService`, `EventAccessService`,
  `RequireEventRoleAttribute` (named `RequireEventRole`, not `EventRole`, because C#
  resolves an attribute of that name against the enum of the same name)
- `[RequireEventRole]` applied across `PhotosController`, `ClustersController` (whole
  controller), the event-scoped actions of `EventsController`, and
  `ConnectionsController.Send` — which previously checked that the *target* had a confirmed
  tag but never that the *sender* had any standing in the event
- `TagsController` opted out via `[NoEventRoleCheck]` with reasons: it scopes to the
  caller's own claimed Attendee record, which is strictly stronger than a membership check
- `AccountController.Register`: event and role self-selection removed from the view model,
  the controller and the view. A new account now has standing in no event at all.
- `ClaimController`: grants `EventMembership(Attendee)` on claim, completing the D3 flow
  that was specified in `BUILD_GUIDE.md` §3 but never written. Repeat visits repair
  attendees who claimed before this existed, so nobody is locked out of tag review.
- `EventsController`: `Index` scoped to the caller's memberships; `Create` now grants
  Coordinator **and** Photographer, without which an event's creator could not upload to it
- `DbInitializer`: seeds the memberships the demo personas need, or the demo event would be
  unreachable to everyone including its own admin
- Tests: `EventAccessServiceTests` (6) and `EventScopedActionGuardTests` (3), including the
  reflection guard over every action taking an `eventId`

  Verified in the browser: a freshly registered account is redirected to AccessDenied from
  the gallery, the upload page, the batched upload endpoint, cluster review, the attendee
  list and the CSV import, and sees an empty events list; the seeded coordinator/photographer
  still uploads normally.

**Phase 1 — independent events.** Migration: `Event.OrganisationId` nullable,
`CreatedByUserId`, `CreatedAt`; `EventMembership.IsPermanent`; unique index
`(EventId, UserId, Role)`. Resolver returns a role set. Create-event form gains the "no
organisation" option and grants both memberships.

**Phase 2 — organisations and roster.** Migration: `OrganisationMembership` + backfill from
`AdminUserId`. `OrganisationsController` per the revised brief, plus the Members screen.
Resolver gains step 2 (org role → effective Coordinator).

**Phase 3 — invitations and handover.** Migration: `Invitation`. `InvitationsController`,
`InvitationService`, the accept-confirmation page, the attach-to-organisation screen, and
the emails through the existing `IEmailSender`.

**Phase 4 — lifecycle.** D13 is currently unenforced: `Event.Status` is set at creation and
never transitions or gets checked, so uploads are accepted in any state. Add the transitions
as Coordinator actions and enforce them in the upload, tag and connection paths.

---

## 10. Tests worth writing

The access model is exactly the kind of thing that looks right and is wrong, so these are
specified up front:

- A signed-in user with no membership gets 403 from every event-scoped action — gallery,
  upload, cluster identify, attendee list, CSV import.
- The reflection guard: every action with an `eventId` parameter carries `[EventRole]` or an
  explicit opt-out.
- An org roster **Photographer** gets no access to an org event they were not assigned to.
- An org **Manager** resolves to Coordinator on an org event, and to nothing on an event the
  organisation does not hold.
- A solo creator resolves to both Coordinator and Photographer.
- Removing a member fails when `IsPermanent` is set.
- An organisation cannot adopt an event without an accepted handover invitation.
- Accepting an invitation over GET does nothing; only the POST accepts.
- An expired, revoked or already-accepted token is rejected.
- Removing someone from an org roster leaves their `EventMembership` rows intact.

---

## 11. Still open

- **Detaching an event from an organisation** — the rule above (org Owner or permanent
  creator) is a first pass. Whether a client organisation should be able to refuse detachment
  is a real product question and is not settled.
- **Invitation to an email that already has an account under a different address** — the
  token authorises, so it works, but there is no verification that the accepter is the person
  the photographer meant. Acceptable for this build; worth a note in the write-up.
- **OPEN-6 (email provider)** in `BUILD_GUIDE.md` becomes critical path here. Phase 3 is
  entirely email-driven and currently runs through `NoOpEmailSender`, which only logs.
