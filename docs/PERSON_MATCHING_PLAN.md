# Person Matching Plan — Face + Appearance Fusion

**Status:** M1–M3 built (25 July 2026) — schema, gate, all three embedding signals, fused
two-phase clustering. **M4** (merge/split, constraint persistence, cluster review polish)
and **M5** (calibration on a pilot set) are not built. No threshold in §5.3 has been
calibrated against real photos yet, so none of them should be trusted.
**Scope:** one-day events only. Multi-day support is explicitly out of scope for v1.

This document is the build spec for the person-matching pipeline. It is written to be
self-contained: a developer (or coding model) should be able to implement from this file
alone, without the conversation that produced it.

---

## 1. Goal

Given all photos uploaded to an event, group every *clearly visible* appearance of the same
person into one cluster, so the photographer can identify a cluster once and the system can
suggest tags across every photo in it.

**Definition of "working" (product owner's words):** when a person was in focus in certain
pictures, or clearly part of a group photo, the system identifies them there. It must **not**
label every backshot, blurry background figure, or out-of-focus passer-by.

## 2. Principles

1. **Face and appearance are co-equal signals, fused per pair.** Neither is "the backbone."
   Every detection gets both embeddings computed when possible; whichever signal has better
   quality *in that specific pair of photos* carries more weight. Appearance = whole-person
   look for the day: hair colour/texture/style, clothing, skin tone (implicitly), build,
   accessories, headwear.
2. **One-day assumption.** Appearance (outfit + hair) is treated as stable for the entire
   event. No session scoping, no outfit-change handling.
3. **The prominence gate decides what users ever see.** Every detection is scored for
   prominence and focus. Only detections that pass the gate ("Tier A") can receive tags or
   appear in any attendee-facing view. Everything else ("Tier B") is stored and may be used
   as internal clustering evidence, but is invisible to users. No exceptions.
4. **Precision over recall for merges.** Wrongly merging two people into one cluster is the
   damaging error (wrong tags suggested to attendees). Fragmenting one person into two
   clusters is cheap — the photographer has a merge tool. Bias every threshold toward
   precision.
5. **No explicit demographic attributes.** Skin tone, race, gender etc. are never stored as
   columns or labels. They exist only implicitly inside opaque embedding vectors. Under
   POPIA, biometric and race data are special personal information; embeddings used for
   within-event matching plus the existing consent flow is the defensible posture. This is
   also technically better — the embedding carries more nuance than any label.

## 3. Pipeline overview

```
Photo uploaded → queue message → Python worker (per photo):
  1. Person detection            (YOLO11, class=person)
  2. Face detection + pose       (InsightFace SCRFD), matched to person boxes
  3. Quality & prominence score  → IsTaggable (Tier A / Tier B)
  4. Embeddings:
       face       (ArcFace 512-d, if face visible & usable)
       appearance (OSNet 512-d on full person crop, HEAD INCLUDED)
       head       (OSNet on top ~25% of person box — hair signal, works on backshots)
  5. Upload embeddings to blob, POST detection metadata to web app

Batch complete (or manual trigger) → recluster message → worker:
  6. Load all detections for the event
  7. Fused pairwise similarity + guardrails
  8. Two-phase clustering
  9. POST cluster assignments to web app
```

## 4. Per-photo processing (worker)

### 4.1 Person detection
- Model: **Ultralytics YOLO11s**, `person` class only, confidence ≥ 0.5.
- Run on the *display* rendition (long side ~2048 px), not the thumbnail.
- Output: person boxes in **relative coordinates** (0–1 floats), so they survive rendition
  changes.

### 4.2 Face detection and association
- Model: **InsightFace `buffalo_l`** (SCRFD detector + ArcFace recognizer + pose).
- Associate each face box to the person box that contains it (highest containment ratio;
  require ≥ 0.7 of the face area inside the person box). Faces with no person box become
  their own detection (person box = expanded face box) — happens in tight head crops.
- Record per face: detection score, box, min side in px, yaw/pitch.

### 4.3 Quality and prominence — the gate

Compute per detection:

| Signal | How |
|---|---|
| `RelHeight` | person box height / image height |
| `Sharpness` | variance of Laplacian (OpenCV) on the grayscale person crop |
| `FaceMinSide` | min(face box w, h) in pixels on the display rendition, 0 if no face |
| `FaceScore` | SCRFD detection score, 0 if no face |
| `Yaw` | absolute yaw in degrees, 999 if no face |

**Tier A (`IsTaggable = true`)** requires ALL of:
- Face present with `FaceScore ≥ 0.6`, `FaceMinSide ≥ 36`, `|Yaw| ≤ 60°`
  (backshots and hard profiles are Tier B by definition — this is the product requirement)
- `Sharpness ≥ S_min` (starting value 60; calibrate in §9 — blur = background/out of focus)
- `RelHeight ≥ 0.12` OR `FaceMinSide ≥ 48`
  (small-but-sharp faces in group photos still qualify — group photos must work)

Everything else is **Tier B (`IsTaggable = false`)**: stored, embedded, may join a cluster
internally, never generates a Tag, never appears in attendee-facing queries. Enforce the
exclusion in the web app's query layer (a global filter or a mandatory `WHERE IsTaggable = 1`
on every attendee-facing query), not just in views.

Also store a continuous `ProminenceScore` (normalised blend of the signals) for ranking
"best crop" per cluster in review UI.

### 4.4 Embeddings
- **Face:** ArcFace 512-d, L2-normalised. Compute whenever a face was detected with
  `FaceScore ≥ 0.5` — even for Tier B (evidence value).
- **Appearance:** **OSNet_x1_0** (via `torchreid`, pretrained, CPU is fine) on the full
  person crop. **The crop must include the whole head** — do not crop at the neck. Resize to
  256×128 as OSNet expects. 512-d, L2-normalised.
  - *Fallback if torchreid install fights back:* open_clip ViT-B/32 image embedding of the
    same crop. Slightly worse for Re-ID, much easier dependency. Keep the interface identical
    (one `embed_appearance(crop) -> np.ndarray` function) so swapping is one line.
- **Head:** same OSNet on the top 25% of the person box (full width). This is the dedicated
  hair/headwear signal — hair colour, braids, locs, fade, headwrap, cap — and it works from
  behind. Skip if the region is under 32 px tall.
- Storage: worker uploads each embedding as `.npy` to blob at
  `events/{eventId}/embeddings/{detectionId}/{face|app|head}.npy`. SQL stores only the refs.

### 4.5 Reporting back
Worker POSTs to the web app (internal API, shared-key header `X-Worker-Key`):

```
POST /internal/api/photos/{photoId}/detections
{
  "detections": [{
    "box": {"x":0.41,"y":0.10,"w":0.18,"h":0.72},
    "faceBox": {...} | null,
    "faceQuality": 0.87,          // 0 if no usable face
    "sharpness": 142.3,
    "prominence": 0.91,
    "isTaggable": true,
    "faceEmbeddingRef": "...face.npy" | null,
    "appearanceEmbeddingRef": "...app.npy" | null,
    "headEmbeddingRef": "...head.npy" | null
  }]
}
```

The web app creates `PersonDetection` rows and marks the photo `Processed`.

## 5. Clustering (per event)

Triggered by a `recluster` queue message: after each upload batch completes, and from a
manual **Re-cluster event** button on the photographer dashboard. One-day events are small
(hundreds of photos, low thousands of detections) — reclustering from scratch each time is
fine and far simpler than incremental clustering. Do not build incremental clustering.

### 5.1 Fused pairwise similarity

```python
def similarity(a, b):
    # Hard vetoes first
    if a.photo_id == b.photo_id:
        return CANNOT_LINK              # two boxes in one photo = two different people
    if a.face_q >= 0.7 and b.face_q >= 0.7 and cos(a.face, b.face) < 0.15:
        return CANNOT_LINK              # two confident faces that clearly disagree:
                                        # appearance can NEVER override this (uniforms,
                                        # dress codes, same-hair collisions)
    sims, weights = [], []
    if a.face is not None and b.face is not None:
        w = 2.0 * min(a.face_q, b.face_q)      # face weight scales with quality
        sims.append(cos(a.face, b.face)); weights.append(w)
    if a.app is not None and b.app is not None:
        sims.append(cos(a.app, b.app)); weights.append(1.0)
    if a.head is not None and b.head is not None:
        sims.append(cos(a.head, b.head)); weights.append(0.5)
    if not sims:
        return 0.0
    return weighted_average(sims, weights)
```

The quality-scaled face weight is what makes the signals co-equal in practice: a sharp
frontal pair is dominated by face; a soft profile pair is dominated by appearance + hair;
in between, both genuinely vote.

### 5.2 Two-phase clustering

**Phase 1 — anchors (high precision):** take Tier A detections with `face_q ≥ 0.7`. Build a
graph with edges where `cos(face) ≥ 0.55`. Connected components = anchor clusters. These are
almost always correct.

**Phase 2 — attach:** for every remaining detection (weaker Tier A faces, then Tier B), find
the best fused similarity to each anchor cluster (average of its top-3 member similarities —
not single best, to resist outliers). Attach if fused sim ≥ **0.60** and no veto fires.
Otherwise the detection stays unclustered.

**Rules:**
- A cluster is only ever *surfaced* if it contains ≥ 1 Tier A detection. Groups made purely
  of Tier B detections (the person who avoided the camera all day, venue staff shot from
  behind) are never shown to anyone and their embeddings fall under the existing purge
  schedule for unlinked faces.
- Store `ClusterConfidence` per attachment (the fused sim) — the review UI sorts by it so the
  photographer checks the shakiest attachments first.
- Cannot-link (same photo) must be enforced at cluster level too: if attaching detection D to
  cluster C would put two same-photo detections in C, refuse the attach.

### 5.3 Starting thresholds (calibrate before trusting — see §9)

| Threshold | Value |
|---|---|
| Face anchor edge | cos ≥ 0.55 |
| Face veto | cos < 0.15 with both `face_q ≥ 0.7` |
| Fused attach | ≥ 0.60 |
| OSNet appearance "same outfit" reference | cos ≈ 0.75+ |
| Sharpness gate `S_min` | 60 (Laplacian variance) |

## 6. Data model changes (web app)

Rename/replace `FaceDetection` → **`PersonDetection`**, `FaceCluster` → **`PersonCluster`**.

```csharp
public class PersonDetection {
    public int Id { get; set; }
    public int PhotoId { get; set; }
    public Photo Photo { get; set; }

    // relative coords 0..1
    public float BoxX { get; set; } public float BoxY { get; set; }
    public float BoxW { get; set; } public float BoxH { get; set; }
    public float? FaceX { get; set; } public float? FaceY { get; set; }
    public float? FaceW { get; set; } public float? FaceH { get; set; }

    public float FaceQuality { get; set; }      // 0 = no usable face
    public float Sharpness { get; set; }
    public float ProminenceScore { get; set; }
    public bool IsTaggable { get; set; }        // the gate — Tier A vs Tier B

    public string? FaceEmbeddingRef { get; set; }
    public string? AppearanceEmbeddingRef { get; set; }
    public string? HeadEmbeddingRef { get; set; }

    public int? PersonClusterId { get; set; }
    public PersonCluster? PersonCluster { get; set; }
    public float ClusterConfidence { get; set; }
}

public class PersonCluster {
    public int Id { get; set; }
    public int EventId { get; set; }
    public Event Event { get; set; }
    public PersonClusterStatus Status { get; set; }   // Unidentified, Identified, Ignored
    public int? AnchorDetectionId { get; set; }       // highest-quality face; review UI cover
    public string? LinkedUserId { get; set; }         // identified attendee
    public ICollection<PersonDetection> Detections { get; set; }
}
```

`Tag` is unchanged: it references a `PersonDetection` + user with the
`Suggested → Confirmed / Rejected` lifecycle. **Tags may only ever be created for detections
with `IsTaggable = true`** — enforce in the service layer, not just the UI. When the
photographer identifies a cluster, suggested tags are generated for the cluster's Tier A
detections only.

> Branch note (updated 25 July 2026): built on `master` against `AlgoForgeDbContext`.
> The earlier advice to prefer `feature/her-code` because `Tag` existed there is now stale —
> `master` has since absorbed `Tag`, `Connection`, `TagsController` and `ClaimController`.
>
> Two deviations from the code above, both deliberate: primary keys are `Guid`, not `int`
> (D14 is LOCKED), and clusters link to `LinkedAttendeeId` rather than `LinkedUserId`,
> because identification has to work before the person has an account (D3).

## 7. Photographer tools (minimum set)

1. **Cluster review grid** — clusters sorted by size; each shows its anchor crop and member
   crops sorted by `ClusterConfidence` ascending toggle ("show shakiest first").
2. **Identify** — link cluster → invited attendee (exists in `her-code` already).
3. **Merge** — select 2+ clusters → one. Fixes fragmentation (the error mode we deliberately
   bias toward).
4. **Split / remove** — remove wrong detections from a cluster; removed detections go back to
   unclustered.
5. **Ignore** — mark a cluster as not-relevant (staff, passers-by); hidden from all flows,
   embeddings purged on schedule.
6. **Re-cluster event** button. Manual corrections (merge/split/identify) must be stored as
   constraints (must-link / cannot-link pairs) and reapplied after every re-cluster, or the
   photographer's work evaporates. Simplest implementation: persist correction pairs in a
   table; the worker receives them in the recluster message and applies them as hard
   constraints.

## 8. Worker contract summary

| Message (queue) | Payload | Worker action |
|---|---|---|
| `process_photo` | `{photoId, eventId, blobPath}` | §4, POST detections |
| `recluster` | `{eventId, constraints:[{a,b,type}]}` | §5, POST assignments |

| Callback (web app internal API) | Purpose |
|---|---|
| `POST /internal/api/photos/{id}/detections` | store detections |
| `POST /internal/api/events/{id}/clusters` | replace cluster assignments atomically |

Auth: single shared secret in `X-Worker-Key` header, from config. Fine for v1.

Python deps (pin them): `ultralytics`, `insightface`, `onnxruntime`, `torchreid` + `torch`
(CPU), `opencv-python-headless`, `numpy`, `azure-storage-blob`, `azure-servicebus`.
CPU throughput ~1–3 s/photo is acceptable at event scale; no GPU requirement.

## 9. Calibration & acceptance (do this before trusting any threshold)

1. Collect a pilot set: ~150–300 photos from one real gathering (a class event, a braai —
   anything), with ~8–15 known people. Hand-label who appears clearly in which photos.
2. Run the pipeline; produce a confusion report: correct clusterings, false merges, false
   splits, gate decisions.
3. Tune, in order: `S_min` (gate blur) → face anchor threshold → fused attach threshold.

**Acceptance criteria for v1 (from the product definition in §1):**
- ≥ 90% of Tier A detections end up in the correct cluster (recall on in-focus appearances,
  including group photos).
- ≤ 2% of detections are merged into a wrong person's cluster (precision — the hard target).
- **Zero** tags exist on Tier B detections. Zero Tier-B crops visible in any attendee view.
- A person who never faces the camera produces no attendee-visible cluster.

## 10. Build order

| Milestone | Deliverable | Proves |
|---|---|---|
| **M1** | Schema (`PersonDetection`/`PersonCluster`) + migration + queue plumbing + worker skeleton that detects persons/faces and stores rows with boxes + gate flags (no embeddings) | end-to-end plumbing; gate visible in DB |
| **M2** | Face embeddings + Phase-1 clustering only + cluster review grid | face path works; parity with original design |
| **M3** | Appearance + head embeddings, fused Phase-2 attach, vetoes, confidence sort | the actual feature |
| **M4** | Identify → suggested tags (Tier A only), merge/split/ignore, constraint persistence + re-cluster | photographer workflow complete |
| **M5** | Calibration run on pilot set, threshold tuning, acceptance report | it works on real photos |

Do not reorder M3 before M2 — a working face-only pipeline is the baseline that tells you
whether fusion is actually helping or hurting.

## 11. Explicitly out of scope for v1

- Multi-day events (appearance changes overnight — needs day-scoped embeddings later; the
  schema above doesn't block it).
- Incremental clustering (recluster-from-scratch is fine at this scale).
- Gait, audio, or any other modality.
- Face blur on rejection, delegate privacy controls (already on the product roadmap, not
  this pipeline's problem).
- Automatic identity from appearance alone — a cluster with no Tier A face is never surfaced,
  ever. This is both a correctness rule and the consent posture.
