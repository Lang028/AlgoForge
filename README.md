# AlgoForge

**Organisation-managed event photography and attendee reconnection platform.**

AlgoForge lets organisations run photo galleries for their events, automatically groups the faces in those photos, and — with each person's explicit consent — tags attendees so they can find their photos and reconnect with the people they met.

> The core idea: event photos are full of people you meant to follow up with and never did. AlgoForge turns a photo gallery into a consent-based networking layer.

---

## How it works

1. An **organisation** creates an event and invites attendees directly by email — there are no open guest links.
2. A **photographer** uploads photos into event albums.
3. The **person pipeline** detects people in every photo and groups them into clusters across the event (the same person across 40 photos = one cluster). Matching fuses two co-equal signals: the face, and whole-person appearance for the day — hair colour/texture/style, clothing, accessories — which stays stable across a one-day event. Whichever signal is stronger in a given photo pair carries more weight. See [docs/PERSON_MATCHING_PLAN.md](docs/PERSON_MATCHING_PLAN.md) for the full design.
4. The photographer identifies a cluster by linking it to an invited attendee. The system then attaches **suggested tags** to that person across every matching photo.
5. The **attendee** reviews their suggested tags and confirms or rejects them — per photo or in bulk. Nothing is published as a confirmed tag without their say-so. Attendees can also self-tag while browsing.
6. After the event, attendees browse the gallery, view profiles, and send **connection requests** to people they met. Contact details are only shared once a connection is accepted.

### Consent model

Consent is the product, not a checkbox:

- Tags have a lifecycle: `Suggested → Confirmed / Rejected`. Rejection removes the association; the photograph itself is unaltered.
- Rejected tags are kept as a status flip, not a delete — an auditable record that consent was requested and declined.
- Each face detection carries its own tag, so two people in the same photo make independent consent decisions.
- Organisers can toggle tag confirmation per event; where it's off, identified tags auto-confirm (suited to small, trusted events).
- Faces that are never linked to an invited attendee (venue staff, passers-by) are never shown to attendees, and their embeddings are purged on a schedule.
- **Prominence gate:** only people who are clearly in frame — in focus, face visible, a real subject of the photo or a group shot — can ever be tagged or shown. Backshots, blurry background figures, and passers-by are detected but stay internal-only, always. Someone who avoided the camera all day is never surfaced by their outfit or hair.
- No demographic attributes (skin tone, race, gender) are ever stored as data fields. Appearance lives only inside opaque embedding vectors — the POPIA-safe posture, and technically better anyway.
- Attendees who don't want a photo of them visible at all can request a takedown, reviewed by the event coordinator.

## Roles

| Role | Access |
|---|---|
| **Event Coordinator** | Configures the event, invites attendees, manages access, moderates comments, reviews takedown requests |
| **Photographer** | Uploads photos, organises albums, reviews and identifies face clusters, corrects clustering mistakes |
| **Attendee** | Browses the gallery, reviews/confirms suggested tags, self-tags, manages privacy, comments, connects with other attendees, invites a delegate |
| **Attendee Delegate** | View-only access granted by an attendee — can browse photos and view profiles |
| **Outsider** | No access to any event content |

Roles are scoped per event through an `EventMembership`, so the same user can be a photographer at one event and an attendee at another, across multiple organisations.

## Architecture

```
┌─────────────────────┐        ┌──────────────────────┐
│  ASP.NET Core MVC   │─blob──▶│  Azure Blob Storage  │
│  (web app + API)    │        │  photos + renditions │
└─────────┬───────────┘        └──────────┬───────────┘
          │ enqueue photo batch           │
          ▼                               │
┌─────────────────────┐        ┌──────────▼───────────┐
│   Message queue     │───────▶│  Face worker (Python) │
│  (Azure Service Bus)│        │  InsightFace: detect, │
└─────────────────────┘        │  embed, cluster       │
                               └──────────┬───────────┘
          ┌───────────────────────────────┘
          ▼ writes detections + clusters
┌─────────────────────┐
│  Azure SQL Database │
│  (EF Core)          │
└─────────────────────┘
```

- **Web app** — ASP.NET Core MVC, Entity Framework Core, Bootstrap 5, ASP.NET Identity with policy-based authorization reading per-event roles.
- **Person pipeline** — a containerised Python service. [InsightFace](https://github.com/deepinsight/insightface) for face detection/embedding, YOLO for person detection, and a Re-ID model (OSNet) for whole-person appearance embeddings (full crop, head included, plus a dedicated head crop for the hair signal). Face and appearance similarities are fused per pair, weighted by quality. Uploads are queued rather than processed synchronously, so a 300-photo batch doesn't block the request path. Embeddings live in blob storage; SQL holds only references.
- **Storage** — Azure Blob with three renditions per photo (original, display, thumbnail), generated by the same worker pass that runs face detection.
- **Database** — Azure SQL. Core entities: `Organisation`, `Event`, `User`, `EventMembership`, `Invitation`, `Album`, `Photo`, `FaceDetection`, `FaceCluster`, `Tag`, `Connection`, `Comment`.

### Event lifecycle

Events move through `Draft → Live → PostEvent → Archived`. Uploads are only accepted while Live; tag review and connections open in PostEvent; Archived events are read-only.

## Getting started

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- [Python 3.11+](https://www.python.org/downloads/) (face pipeline)
- SQL Server (LocalDB is fine for development)

> **What the Architecture section above describes is the target design.** As it stands
> today photos are written to `AlgoForge/App_Data/uploads` on the local disk and served
> through an authorising action, and the pipeline is called directly rather than through a
> queue. So there is **no Azure Storage or Azurite to install** to run this locally.

### Run the web app

```bash
git clone https://github.com/Lang028/AlgoForge.git
cd AlgoForge

# apply migrations
dotnet ef database update --project AlgoForge/AlgoForge

# run
dotnet run --project AlgoForge/AlgoForge
```

### Run the person pipeline

The pipeline is a local FastAPI service in `face_service/`. The web app calls it during
upload, so start it first or photos will be stored without any people detected.

```bash
cd face_service
python -m venv .venv && .venv/Scripts/activate      # source .venv/bin/activate on macOS/Linux
pip install -r requirements.txt
uvicorn main:app --port 8000
```

**The first start is slow and needs internet.** `yolo11s.pt` is committed so it arrives
with the clone, but InsightFace fetches its `buffalo_l` models (~300MB) on first use and
caches them in `~/.insightface`. Installing `torch` pulls roughly 2GB on top of that.
Budget time for the first run on a new machine; every run after that is offline and fast.

Check what it can actually do:

```bash
curl http://127.0.0.1:8000/health
```

`signals` tells you which models loaded. Only the face signal is required — the service
runs without YOLO or the appearance backend, it just loses recall on profiles and
backshots. `torch` in particular must come from the CPU index, or pip pulls the CUDA
build:

```bash
pip install torch==2.5.1 --index-url https://download.pytorch.org/whl/cpu
```

If `torchreid` refuses to install, uncomment `open_clip_torch` in `requirements.txt`
instead — the service picks it up automatically, no code change needed.

Run the clustering tests (no models required — they use synthetic embeddings):

```bash
cd face_service && python -m pytest tests -q
```

## Deployment

**Nobody using the site installs anything.** Attendees, photographers and organisations
open a browser. That is the whole client requirement — no Python, no models, no .NET.

Python is a *server* concern. The face service is an internal component the web app talks
to; it is never reachable by, or visible to, a visitor:

```
   Browser  ──HTTPS──▶  ASP.NET Core app  ──HTTP──▶  face_service (Python)
  (nothing                     │              private,   InsightFace + YOLO
   installed)                  ▼              not public
                          SQL Server
```

So a deployment installs, on the server side only:

| Component | What the host needs |
|---|---|
| Web app | ASP.NET Core 8 runtime (or self-contained publish) |
| Face service | Python 3.11 + `requirements.txt` + the InsightFace models |
| Database | SQL Server — a real instance, not LocalDB |
| Photos | A writable path for `App_Data/uploads`, or blob storage |

Three ways to arrange that, cheapest first:

1. **One machine.** Publish the .NET app behind IIS or Nginx, and run
   `uvicorn main:app --port 8000` as a service (systemd, or NSSM on Windows) bound to
   `127.0.0.1` so only the web app can reach it. Simplest, and enough for a demo or a
   small production load.
2. **Two containers.** One image for the app, one for `face_service`, on the same private
   network. The models are best baked into the Python image at build time so a cold start
   is not a 300MB download.
3. **Separate hosts.** The face service on its own box — worth it only when detection load
   justifies scaling it independently of the site.

The app finds the service through configuration, so nothing is hard-coded:

```json
"PersonPipeline": { "BaseUrl": "http://127.0.0.1:8000" }
```

Point that at wherever the service actually lives in each environment.

### Before going live

- **Database.** Move off LocalDB. It is a developer convenience that starts on demand and
  stops when idle, which is not what a server does.
- **Email.** `OutboxEmailSender` writes messages to disk for the demo. Swap in a real
  provider or no invitation will ever arrive.
- **Face processing is synchronous.** It runs inside the upload request, which is fine for
  a handful of photos and will time out on a large batch. The queue in the architecture
  diagram is the fix, and the seam for it is the pipeline call in `PhotosController`.
- **Secrets.** The connection string and any provider keys belong in environment variables
  or a secret store, not `appsettings.json`.

Configuration (connection strings, blob credentials, queue names) is read from `appsettings.Development.json` and environment variables — see `appsettings.example.json` for the required keys. Never commit real credentials.

## Project structure

Project layout:

```
AlgoForge/
  AlgoForge/              ASP.NET Core MVC app (Controllers, Views, Models, Data)
docs/
  PERSON_MATCHING_PLAN.md face + appearance fusion pipeline — the build spec
```

## Roadmap

Scoped out of the current build, deliberately:

- Notification read/unread tracking
- Comment moderation beyond delete
- Delegate-specific privacy controls
- Face blur on tag rejection (rejection currently removes the association only)
- Multi-day events for appearance matching (people change clothes overnight; v1 assumes one-day events where outfit + hair are stable — chasing the perfect system first would be the downfall)

## Licence

TBD.
