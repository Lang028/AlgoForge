# Geeked On — Build Guide

**Single source of truth for the Geeked On build.** Any human or AI joining this project reads this document first. If a decision is recorded here as LOCKED, do not re-litigate it — build to it. If something is marked OPEN, it has not been decided; resolve it with Lange before writing code that depends on it.

- Project: Geeked On — organisation-managed event photography and attendee reconnection platform
- Team: AlgoForge, Group 12 — Application Development, Durban University of Technology
- Deadline anchor: Demo Day (all sprint planning works backwards from this date — **date TBD, fill in**)
- Owner: Langelihle (Lange) Gumede

---

## 1. What this product is

Organisations run photo galleries for their events. The system detects and clusters faces across all event photos. With each attendee's explicit consent, faces are tagged so attendees can find their photos and reconnect with people they met. Consent and privacy apply to **personal information (contacts, identity)** — the photos themselves are event photos, shared in the spirit of the event. Access is invitation-only; there are no open guest links, no feed, no engagement loop.

**The one-line pitch:** event photos are full of people you meant to follow up with and never did; Geeked On turns a photo gallery into a consent-based networking layer — without becoming another social media site.

### The core flow

1. **Photographer** shoots the event, uploads photos, and invites the **organisation** onto the platform. *(The photographer is the entry point — LOCKED for this build. A future inversion is known and expected: an organisation running many events will want to create the event first and invite photographers into it. Design nothing that makes that inversion hard, but do not build it now — see §7.)*
2. The organisation's **event coordinator** creates the event and **uploads the invitee list** — names, emails, contact info — creating Attendee records before any of those people have accounts.
3. The system detects faces and clusters them across the event.
4. The **coordinator** (primarily) matches clusters to people on the invitee list; the photographer can do this too, as backup.
5. Matching a cluster creates **Suggested** tags for that attendee across every photo they appear in.
6. Attendees receive an **email link** — clicking it is how they claim their record and enter the system. They view photos, confirm/reject their tags, edit their own information, and connect with people they met.
7. Attendees may invite **delegates** — trusted colleagues who can view and download photos.

---

## 2. Decision register

Every architectural and product decision made so far, with rationale. Status is LOCKED unless stated otherwise.

### D1. Roles — LOCKED
Five actors: **Event Coordinator**, **Photographer**, **Attendee**, **Attendee Delegate**, **Outsider** (no access). Photographer and Coordinator are **separate roles with disjoint permissions** (deliberate separation of duties — supersedes an earlier draft where they shared identical permissions; requirements document must be updated to match).

### D2. Roles are per-event, not global — LOCKED
Permissions come from `EventMembership` (user + event + role). The same user can be photographer at one event and attendee at another, across multiple organisations. Implement authorization as ASP.NET Identity + **policy-based authorization reading EventMembership.Role** — do not use global Identity roles.

### D3. Attendee ≠ User (the claim model) — LOCKED
An **Attendee** is an event-scoped record created from the coordinator's uploaded invitee list — it exists **before the person has an account**. Clusters are linked and tags are suggested against the Attendee record. The emailed invite link is how a person **claims** their Attendee record with a User account (`ClaimedByUserId`). Tags on an unclaimed Attendee stay Suggested forever — they can never be confirmed without the person, so consent is structurally enforced.

### D4. Identification authority — LOCKED
The **coordinator is the primary identifier** — they own the attendee list, so they say who is who. The **photographer keeps the capability as backup** (useful when the photographer personally knows faces the coordinator doesn't).

### D5. Delegate: view + download, zero writes — LOCKED
A delegate browses and **downloads original photo files**. Rationale: it's an event — the photos are meant to be shared; the privacy model protects *contact information*, not the photographs. Delegates have **no write actions**: no tagging, no commenting, no connecting. `EventMembership.GrantedByUserId` records the granting attendee; revocation is a one-row delete. *(Future, explicitly deferred: delegate connecting on behalf of the attendee, only if the attendee opts in — see §7.)*

### D6. Tag lifecycle — LOCKED
`Suggested → Confirmed / Rejected`, with `Origin` tracked separately (`ClusterMatch` or `SelfTag`).
- Tags hang off `FaceDetection`, never off `Photo` — two people in one photo make independent consent decisions.
- Tags reference the **Attendee** record (D3), not User directly.
- Self-tags land directly as Confirmed (origin SelfTag).
- Rejection is a **status flip, not a delete** — auditable record that consent was requested and declined (POPIA answer).
- Rejection removes the association; **the photograph is unaltered** (no blur — see §7).
- Per-event toggle `Event.TagConfirmationRequired`: when off (trusted/small events), identified tags auto-confirm.

### D7. Unidentified clusters — LOCKED (rewritten; supersedes an earlier auto-purge idea)
An unidentified cluster is **not necessarily a stranger** — it may be an attendee whose contacts weren't uploaded, or whose match simply hasn't happened yet. Identification can arrive late (coordinator gets the missing email a week after, someone self-tags).
- **No automatic purge.** Unidentified clusters persist through Live and PostEvent.
- Cleanup happens only at **Archive**, as a deliberate coordinator action — never a scheduler.
- The privacy guarantee that holds throughout: **unidentified faces are never shown to attendees or delegates** — visible only to coordinator and photographer as "unidentified clusters."

### D8. Photo takedown — LOCKED
Distinct from tag rejection. An attendee can request removal of a photo they appear in; the coordinator approves or denies; approved photos get **`Hidden` status (soft hide), not hard delete**.

### D9. Cluster correction — LOCKED (design), use case diagram update pending
Coordinator and photographer get **Merge Clusters** and **Remove Detection from Cluster** actions, because InsightFace will make mistakes. Merging an already-identified cluster cascades new Suggested tags to the linked attendee. These use cases must be added to the use case diagram.

### D10. Connections are global — LOCKED
A connection is between two **claimed** users, **not scoped to an event**. `MetAtEventId` is metadata recording where they met. Unique constraint on the user pair (order-normalised). You cannot connect with an unclaimed attendee — there's no one on the other end to accept.

### D11. Contact visibility is attendee-controlled — LOCKED
Default: event members see name, photo, short bio; **contact details only after an accepted connection**.
Opt-in: an attendee can choose to **make their email/contacts visible to event members** — for trusted events where they'd rather people just reach them directly than go through connection requests. The toggle belongs to the attendee, per event. When contacts are visible, the connection flow becomes unnecessary for reaching that person — by their own choice.

### D12. Notifications are email — LOCKED (product principle, not a deferral)
There is **no in-app notification layer**: no bell, no feed, no read/unread inbox. Every notification is an email carrying an **actionable tokenized link** — the same mechanism as the invite link:
- Invitation → claim link
- Suggested tags → "review your tags" link
- Connection request → **accept/decline directly from the email**
Rationale: cheaper to build than an in-app inbox, and it enforces the product identity — *this is not another social media site*. Say exactly that at Demo Day.

### D13. Event lifecycle — LOCKED
`Draft → Live → PostEvent → Archived`.
- Uploads accepted only in **Live**.
- Tag review and connections open in **PostEvent** (and Live).
- **Archived** is read-only; unidentified-cluster cleanup happens here (D7).

### D14. Primary keys are GUIDs — LOCKED
All entities use `Guid` (`uniqueidentifier`). Reasons: the Python worker mints IDs client-side without DB round trips; IDs appear in URLs and email links and must not be enumerable; batch inserts wire FKs in memory. EF Core default sequential GUIDs mitigate index fragmentation.

### D15. Embeddings live outside SQL — LOCKED
`FaceDetection.EmbeddingRef` is a **reference string** pointing to blob/sidecar storage owned by the Python service. Do not store 512-dim float arrays in SQL Server rows. Similarity search happens in Python.

### D16. Photo pipeline is asynchronous — LOCKED
Upload → queue message → Python worker → writes results to SQL. **Never process faces synchronously in the upload request path.** Queue messages carry photo ID + blob URL, **never image bytes**.

### D17. Photo renditions — LOCKED
Three per photo in Azure Blob: original, display (~1600px), thumbnail (~300px). Generated by the same worker pass that runs face detection. Delegates download **originals** (D5).

### D18. Comments — LOCKED (naming)
Called **Event Comments**, not "Public Comments" — there is no public access. Moderation = coordinator can delete. Nothing more for now.

### D19. Cost posture: near-zero Azure spend — LOCKED
Budget reality: Azure for Students credit only, minimise burn.
- **Database:** Azure SQL Database **free offer** — 100k vCore-seconds serverless + 32 GB/month, per database, permanent (not the 12-month trial). Configure **auto-pause on free-limit reached**, never "continue with charges." ⚠️ Close SSMS/Azure Data Studio connections when done — open connections block auto-pause and burn free credits.
- **Queue:** **Azure Service Bus, Basic tier** — per-operation pricing is cents at project scale, and built-in dead-lettering covers part of OPEN-5. (Storage Queue remains the fallback if anything blocks Basic tier.)
- **Worker:** **runs locally on a team laptop** for all development and for Demo Day (pre-processed event regardless — §6). It only needs connection strings; the queue decouples location. Deploying to Container Apps is an optional stretch goal for the write-up, not a running cost. Side benefit: no cold starts — InsightFace models load once and stay warm.
- **Web app:** App Service **F1 free tier** during development; bump to **B1 for Demo Day week only** (~$3 of credit for the week) so the panel never sees a cold start.
- **Blob:** hot tier, a few GB — negligible.
- **Email:** free tier of a transactional provider (Brevo 300/day or Resend) — see OPEN-6.
Expected monthly spend until Demo Day week: ~R0.

---

## 3. Domain model

Thirteen entities. Full ERD lives in `docs/erd/` — **the ERD must be updated to add Attendee and repoint FaceCluster/Tag (D3).**

| Entity | Purpose | Key fields beyond Id |
|---|---|---|
| Organisation | Hosts events | Name, ContactEmail |
| User | An account holder | DisplayName, Email, IdentityId |
| **Attendee** | Event-scoped person record from the invitee list; exists before any account | EventId, Name, Email, ContactInfo, InviteToken, **ClaimedByUserId (nullable)**, ContactsVisible (D11) |
| Event | One event under an org | OrganisationId, Name, EventDate, TagConfirmationRequired, EventType, Status (D13) |
| EventMembership | Permission spine: user + event + role | EventId, UserId, Role (Photographer/Coordinator/Attendee/Delegate), GrantedByUserId (delegates only), JoinedAt |
| Invitation | Email invite (organisation or role invites) | EventId, Email, Role, Token, Status, ExpiresAt |
| Album | Grouping within an event | EventId, Name |
| Photo | One uploaded photograph | EventId, AlbumId (nullable), UploadedByUserId, BlobUrl, CapturedAt, UploadedAt, Status (Visible/Hidden, D8) |
| FaceDetection | One face in one photo | PhotoId, FaceClusterId (nullable), BoundingBox, EmbeddingRef, Confidence |
| FaceCluster | One person across an event's photos | EventId, Status (Unidentified/Identified), **LinkedAttendeeId (nullable)**, IdentifiedByUserId |
| Tag | One consent decision | FaceDetectionId, **TaggedAttendeeId**, Status (Suggested/Confirmed/Rejected), Origin (ClusterMatch/SelfTag), CreatedByUserId, ResolvedAt |
| Connection | Global link between two claimed users | RequesterId, ReceiverId, MetAtEventId, Status, RespondedAt |
| Comment | Event comment on a photo | PhotoId, AuthorUserId, Body, CreatedAt |

Notes:
- Attendee claim flow: invite email carries `InviteToken` → person registers/logs in → `ClaimedByUserId` set → an `EventMembership(Role=Attendee)` is created. Membership rows for attendees exist only after claiming.
- "View Unidentified Clusters" = `WHERE Status = 'Unidentified'` on FaceCluster.
- No Notification table, by design (D12) — notifications are emails derived from state changes.

---

## 4. Architecture

```
ASP.NET Core MVC (web + API)
   │  writes photo → Azure Blob (original + display + thumb)
   │  enqueues {photoId, blobUrl} → queue (Azure Service Bus, or
   │                                Storage Queue fallback — see OPEN-1)
   │  sends email (invites, tag review, connection requests)
   ▼
Python worker (containerised, InsightFace)
   detect faces → embed → cluster (per event) →
   writes FaceDetection / FaceCluster rows → Azure SQL
   stores embeddings in blob sidecar (D15)
```

- **Web:** ASP.NET Core MVC, EF Core, Bootstrap 5, ASP.NET Identity, policy-based auth (D2).
- **Worker:** Python 3.11+, InsightFace. Clustering approach and hosting are OPEN (§5).
- **Storage:** Azure Blob (photos, renditions, embeddings), Azure SQL (relational data).
- **Email:** SMTP provider free tier. Email is load-bearing infrastructure here (D12) — invite claims, tag review, and connection accept/decline all run through tokenized email links, so pick the provider early and test deliverability.

---

## 5. Open decisions — resolve before dependent code

**OPEN-1. Queue technology.** ~~RESOLVED → D19: Azure Service Bus Basic tier (Storage Queue as fallback).~~

**OPEN-2. Worker hosting.** ~~RESOLVED → D19: worker runs locally on a team laptop; Container Apps deployment is an optional stretch goal, not a dependency.~~

**OPEN-3. Clustering algorithm.** InsightFace produces embeddings; grouping is separate. Default direction: DBSCAN/HDBSCAN over cosine similarity (cluster count unknown up front). Thresholds need experimentation.

**OPEN-4. Incremental clustering.** When 50 new photos arrive after initial clustering: full re-cluster vs assign-to-existing-clusters. ⚠️ **Constraint: existing FaceCluster IDs must remain stable once a cluster is Identified** — re-clustering that changes IDs orphans Tags and destroys consent data. Hardest open problem in the project.

**OPEN-5. Pipeline failure handling.** Worker dies mid-batch / corrupt image / zero faces found — retry policy, dead-lettering, and what the UI shows.

**OPEN-6. Email provider.** Which SMTP service, and sender-domain setup so invite links don't land in spam (D12 makes this critical path, not a nice-to-have).

**OPEN-7. Licence** for the repo.

---

## 6. Build order

Three planning sessions remain, in this order, then build:

1. **Face pipeline architecture session** — resolves OPEN-3 through OPEN-5 (queue and hosting already settled by D19). First because it is the highest-risk unknown and cluster ID stability has blast radius on the Tag schema.
2. **Requirements document revision** — fold in: Photographer/Coordinator separation (D1), Attendee claim model (D3), coordinator-primary identification (D4), delegate view+download wording (D5), consent flow use cases (*Review/Confirm Suggested Tags*, *Self-Tag in Photo*), cluster correction use cases (D9), takedown workflow (D8), contact visibility toggle (D11), email-first notifications (D12), event lifecycle (D13), "Event Comments" rename (D18), Future Work section (§7).
3. **Sprint plan** — backwards from Demo Day.

**Demo-critical path (protect at all costs):** auth + events + invitee upload → photo upload + gallery → face pipeline + cluster identification → suggested tags + email claim link + consent review. That sequence IS the demo.

**Drop-first list if time runs out (in drop order):** delegates → comments → connections. The demo survives without all three; it dies without consent review.

**Demo Day risk note:** live clustering can misfire on stage. Cluster correction UI (D9) is the insurance. Prepare a pre-processed event as backup — never run the pipeline live for the first time in front of the panel.

---

## 7. Deferred — Future Work section (do not build)

Deliberately out of scope. These go in the requirements doc as scoped decisions, not omissions:

- **Delegate acting on behalf of the attendee** (e.g. initiating/accepting connections) — only ever with explicit attendee opt-in
- **Organisation-initiated events** — an org with many events creates the event first and invites photographers into it (inverts the current photographer-first entry point)
- Comment moderation beyond delete
- Face blur on tag rejection (rejection removes association only, D6)
- Any in-app notification layer (permanently out by D12, not just deferred)

---

## 8. Working rules for humans and AI on this project

1. **Read the decision register before proposing anything.** If your idea contradicts a LOCKED decision, the decision wins unless Lange explicitly reopens it.
2. **OPEN items block dependent code.** Do not scaffold `FaceCluster` migrations or worker code that assumes an answer to OPEN-4.
3. **Update this document when a decision is made.** A decision that isn't recorded here doesn't exist.
4. **Consent invariants are non-negotiable:** no confirmed tag without attendee action (unless TagConfirmationRequired is off); unclaimed attendees' tags stay Suggested; unidentified faces never shown to attendees or delegates; contact details hidden unless an accepted connection exists **or the attendee opted in to visibility** (D11); no hard deletes of consent records.
5. **Product identity invariant:** no feeds, no bells, no engagement mechanics. Email is the notification system (D12). If a feature idea makes it feel like a social network, it's out.
6. **Code style:** ASP.NET Core MVC + EF Core in C#; Python for the worker; copy-paste-ready code blocks in AI sessions. Conservative, honest estimates — no inflated numbers anywhere.
7. **Design style for all UI:** dark mode, natural warm greys, red accents used sparingly, photography as the hero. No glow, no gradients, no glassmorphism, no purple. If it looks like a generic AI landing page, it's wrong.
8. Lange's depth is product thinking and system design; implementation is AI-assisted. Explain non-obvious technical choices rather than assuming — the rationale matters as much as the code.

---

*Last updated: 13 July 2026 — after the flow correction session (Attendee/User split, coordinator-primary identification, email-first notifications, attendee-controlled contact visibility, delegate downloads, D7 rewrite). Next update expected after the face pipeline session.*
