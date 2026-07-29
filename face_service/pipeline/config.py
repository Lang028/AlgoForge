"""
Tunable thresholds for the person pipeline.

Every value here is a starting point from PERSON_MATCHING_PLAN.md section 5.3, not a
tuned constant. Section 9 says calibrate before trusting any of them, in this order:
S_MIN (the blur gate), then FACE_ANCHOR_EDGE, then FUSED_ATTACH. They live in one
module so a calibration run can sweep them without touching pipeline code.

Environment overrides exist so calibration can run the whole service at a different
threshold without editing the file (GEEKEDON_S_MIN=80 uvicorn main:app ...).
"""

import os
from pathlib import Path


def _env_float(name: str, default: float) -> float:
    raw = os.environ.get(name)
    if raw is None:
        return default
    try:
        return float(raw)
    except ValueError:
        return default


# --- Person detection (plan 4.1) ---------------------------------------------------
PERSON_CONF = _env_float("GEEKEDON_PERSON_CONF", 0.5)
# YOLO's COCO class index for "person". Nothing else is of interest to this pipeline.
YOLO_PERSON_CLASS = 0
YOLO_WEIGHTS = os.environ.get("GEEKEDON_YOLO_WEIGHTS", "yolo11s.pt")
SERVICE_ROOT = Path(__file__).resolve().parent.parent
CACHE_ROOT = Path(os.environ.get("GEEKEDON_FACE_CACHE_DIR", SERVICE_ROOT / ".cache"))

# The plan runs detection on the display rendition (long side ~2048px). Renditions are
# not built yet, so the service downscales oversized originals to this instead. Doing it
# here rather than at the call site keeps every pixel threshold below on one scale --
# FACE_MIN_SIDE in particular is meaningless without a fixed working resolution.
WORKING_LONG_SIDE = int(os.environ.get("GEEKEDON_WORKING_LONG_SIDE", "2048"))

# --- Face association (plan 4.2) ---------------------------------------------------
# A face belongs to the person box that contains this much of its area.
FACE_CONTAINMENT = _env_float("GEEKEDON_FACE_CONTAINMENT", 0.7)

# --- The gate (plan 4.3) -----------------------------------------------------------
# Tier A requires ALL of these. Anything failing even one is Tier B: stored and usable
# as internal clustering evidence, but never taggable and never shown to an attendee.
GATE_FACE_SCORE = _env_float("GEEKEDON_GATE_FACE_SCORE", 0.6)
GATE_FACE_MIN_SIDE = _env_float("GEEKEDON_GATE_FACE_MIN_SIDE", 36)
GATE_MAX_YAW = _env_float("GEEKEDON_GATE_MAX_YAW", 60)
S_MIN = _env_float("GEEKEDON_S_MIN", 60)
GATE_REL_HEIGHT = _env_float("GEEKEDON_GATE_REL_HEIGHT", 0.12)
# ...OR a face this big. Small-but-sharp faces in group photos must still qualify,
# because group photos are explicitly part of the product definition of "working".
GATE_FACE_SIDE_ALT = _env_float("GEEKEDON_GATE_FACE_SIDE_ALT", 48)

# --- Embeddings (plan 4.4) ---------------------------------------------------------
# Lower bar than the gate: a Tier B face still carries evidence value for clustering.
EMBED_FACE_SCORE = _env_float("GEEKEDON_EMBED_FACE_SCORE", 0.5)
# Fraction of the person box treated as "head" for the hair/headwear signal.
HEAD_FRACTION = _env_float("GEEKEDON_HEAD_FRACTION", 0.25)
HEAD_MIN_PX = int(os.environ.get("GEEKEDON_HEAD_MIN_PX", "32"))

# --- Clustering (plan 5.3) ---------------------------------------------------------
FACE_ANCHOR_EDGE = _env_float("GEEKEDON_FACE_ANCHOR_EDGE", 0.55)
FACE_VETO_SIM = _env_float("GEEKEDON_FACE_VETO_SIM", 0.15)
FACE_VETO_QUALITY = _env_float("GEEKEDON_FACE_VETO_QUALITY", 0.7)
ANCHOR_MIN_QUALITY = _env_float("GEEKEDON_ANCHOR_MIN_QUALITY", 0.7)
FUSED_ATTACH = _env_float("GEEKEDON_FUSED_ATTACH", 0.60)
# Attachment compares against the mean of a cluster's top-K member similarities rather
# than its single best, so one outlier member cannot pull in a stranger.
ATTACH_TOP_K = int(os.environ.get("GEEKEDON_ATTACH_TOP_K", "3"))

# Signal weights in the fused score. Face weight is multiplied by the pair's weaker
# face quality, which is what makes the signals co-equal in practice rather than by
# declaration: a sharp frontal pair ends up face-dominated, a soft profile pair ends up
# appearance-dominated, and in between both genuinely vote.
W_FACE = _env_float("GEEKEDON_W_FACE", 2.0)
W_APPEARANCE = _env_float("GEEKEDON_W_APPEARANCE", 1.0)
W_HEAD = _env_float("GEEKEDON_W_HEAD", 0.5)
