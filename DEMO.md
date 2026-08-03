# AlgoForge / AlgoForge — demo guide

Run it:

```bash
dotnet run --project AlgoForge/AlgoForge.csproj
```

The dev database migrates and seeds itself on startup. The face pipeline
(`face_service`) is optional — everything below works without it; only new uploads
need it to detect faces.

## Accounts

| Who | Email | Password | Sign in at |
|---|---|---|---|
| Platform admin | `admin@algoforge.local` | `DevAdmin@2026` | `/Admin/Login` |
| Coordinator + Photographer | `seed-admin@algoforge.local` | `SeedAdmin123!` | `/Account/Login` |
| Photographer only | `demo-photographer@algoforge.local` | `DemoPhotographer123!` | `/Account/Login` |
| Attendee | `demo-attendee@algoforge.local` | `DemoAttendee123!` | `/Account/Login` |

The photographer-only account exists to show that the two staff roles really are
disjoint: they can upload to the event but cannot open its attendee list, while a
coordinator can do the reverse.

On `/Account/Login` you also pick the role to sign in **as**. It is a lens, not a
permission — real access always comes from `EventMembership`.

## Email in demo mode

There is no SMTP server. Every email is written to `AlgoForge/App_Data/outbox` and
listed under **Outbox** in the admin navigation, rendered as HTML with the links
clickable. That is how you follow an invite, claim or connection link live.

## A ten-minute walkthrough

1. **Anonymous** — open `/`. You get the public landing page; nothing about any
   event is visible.

2. **Coordinator** — sign in as `seed-admin` choosing **Coordinator**. Open
   Events → Demo Event → Attendees. Press **Send invite**. Sign in as the admin in
   another browser and open **Outbox** to see the email, or use the claim link in
   the green banner.

3. **Attendee consent** — the claim link signs the attendee into the event and lands
   on their tags. Suggested tags can be confirmed or rejected. A name only ever
   appears on a face whose tag is **Confirmed** — rejected and unreviewed faces show
   no box at all.

4. **Gallery** — Photos for the event. Every image is served through
   `Photos/File/{id}`, which checks event membership on each request.

5. **Connections** — as the **attendee**, request a connection from a confirmed face.
   The other person gets an email; contact details stay hidden until they accept.
   Sign in as the coordinator and open Connections: you get "Connections are for
   attendees" instead, and the nav link isn't there.

6. **Delegates** — still as the attendee, open **My delegates** from the tags page.
   Nominate someone by name and email (three places, and the form disappears when
   they're used). Their invite lands in the Outbox. Following it — as a different
   account — opens the gallery with "You're viewing this gallery on behalf of …".
   The delegate gets the pictures and nothing else: no Connections, no tag
   confirmation, no delegate list of their own. Press **Revoke** and their access
   stops at once, including the image URLs.

7. **Admin console** — sign in at `/Admin/Login`. Counts, a 14-day upload chart and a
   per-event table, with no personal details anywhere. The admin has no event
   navigation and cannot create an event.

## Security model

- **Events are private.** Every event-scoped action goes through `RequireEventRole`,
  which reads `EventMembership`.
- **Photos are authorised per request.** They live in `App_Data/uploads`, outside
  `wwwroot`, and are *not* mapped to a static route — `PhotosController.File` checks
  membership and sends `no-store`. (Before this they were served publicly at
  `/uploads/...` to anyone holding the URL.)
- **Connections are attendee-only.** Coordinators and photographers run the event;
  they are not attending it, and the people in the gallery consented to being
  identified to fellow attendees, not approached by staff.
- **The platform admin cannot join events.** They hold no `EventMembership`, and
  `Events/Create` is closed to them so they cannot grant themselves one.
- **Consent gates identity, not photographs.** Tags stay `Suggested` on an unclaimed
  attendee forever.

## Checklist status

Working: photographer and organisation registration; per-event roles; attendee
invite with a token link; claim flow; attendee gallery; tag confirm/reject with
audit; connection request → email → accept/decline → contact exchange; contact
details hidden until acceptance; delegates (invite, limit of 3, claim, revoke);
admin console.

**Not built yet — the honest gaps:**

- **Bulk download** of an attendee's permitted photos.
- **Block / report** another attendee.
- **Invite tokens are stored in plain text** (`Attendee.InviteToken`) and never expire
  or get revoked. The checklist asks for hashed, expiring, revocable tokens.
- **Account lockout is disabled** (`lockoutOnFailure: false`) on both login doors, so
  passwords can be guessed without limit. The admin password is also seeded in source.
- **PIN fallback** for attendees without unique links.
