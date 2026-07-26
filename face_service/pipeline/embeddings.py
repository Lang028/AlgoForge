"""
The three embedding signals, and the sidecar store that owns them (plan 4.4).

  face        ArcFace 512-d, from InsightFace -- who this is
  appearance  OSNet 512-d over the whole person crop, head included -- outfit and build
  head        OSNet over the top ~25% of the person box -- hair, headwear; works from behind

Appearance has a documented fallback chain (plan 4.4): OSNet via torchreid, then
open_clip, then nothing. The interface is one function, embed_appearance(crop), so which
backend is live never leaks into the pipeline. If none load, fused similarity simply runs
on fewer signals and the weighted average renormalises itself -- see clustering.py.

Storage is the Python service's, per D15: SQL holds only the ref string. Files are .npy
under embeddings/{event_id}/{detection_id}/{face|app|head}.npy.
"""

from __future__ import annotations

import logging
import os
from pathlib import Path

import cv2
import numpy as np

from . import config

logger = logging.getLogger(__name__)

EMBEDDINGS_ROOT = Path(
    os.environ.get("GEEKEDON_EMBEDDINGS_DIR", Path(__file__).resolve().parent.parent / "embeddings")
)

_appearance_backend: str | None = None
_osnet = None
_clip = None
_clip_preprocess = None
_appearance_failed = False


def _load_osnet():
    """OSNet_x1_0 via torchreid, CPU. The plan's first choice for appearance."""
    global _osnet
    import torch
    import torchreid

    model = torchreid.models.build_model(
        name="osnet_x1_0", num_classes=1000, pretrained=True, loss="softmax"
    )
    model.eval()
    _osnet = (model, torch)
    return _osnet


def _load_clip():
    """open_clip ViT-B/32 -- the sanctioned fallback when torchreid won't install.

    Weaker than OSNet at Re-ID specifically (it was never trained to tell two people in
    similar clothing apart), but a far easier dependency, and the fused score's other
    signals cover for it.
    """
    global _clip, _clip_preprocess
    import open_clip
    import torch

    model, _, preprocess = open_clip.create_model_and_transforms(
        "ViT-B-32", pretrained="laion2b_s34b_b79k"
    )
    model.eval()
    _clip = (model, torch)
    _clip_preprocess = preprocess
    return _clip


def _ensure_appearance_backend() -> str | None:
    global _appearance_backend, _appearance_failed
    if _appearance_backend is not None or _appearance_failed:
        return _appearance_backend

    try:
        _load_osnet()
        _appearance_backend = "osnet"
        logger.info("Appearance backend: OSNet (torchreid).")
        return _appearance_backend
    except Exception as exc:  # noqa: BLE001
        logger.warning("torchreid/OSNet unavailable (%s); trying open_clip.", exc)

    try:
        _load_clip()
        _appearance_backend = "clip"
        logger.info("Appearance backend: open_clip ViT-B/32 (fallback).")
        return _appearance_backend
    except Exception as exc:  # noqa: BLE001
        _appearance_failed = True
        logger.warning(
            "No appearance backend available (%s). Clustering will run on the face "
            "signal alone -- recall on profiles and backshots will be materially worse.",
            exc,
        )
    return None


def _l2(vector: np.ndarray) -> np.ndarray:
    norm = float(np.linalg.norm(vector))
    if norm < 1e-8:
        return vector.astype(np.float32)
    return (vector / norm).astype(np.float32)


def embed_appearance(crop: np.ndarray) -> np.ndarray | None:
    """Embed a BGR person crop. Returns an L2-normalised vector, or None if unavailable."""
    backend = _ensure_appearance_backend()
    if backend is None or crop.size == 0:
        return None

    try:
        if backend == "osnet":
            model, torch = _osnet
            # OSNet's expected input geometry: 256x128, ImageNet normalisation, RGB.
            resized = cv2.resize(crop, (128, 256), interpolation=cv2.INTER_LINEAR)
            rgb = cv2.cvtColor(resized, cv2.COLOR_BGR2RGB).astype(np.float32) / 255.0
            mean = np.array([0.485, 0.456, 0.406], dtype=np.float32)
            std = np.array([0.229, 0.224, 0.225], dtype=np.float32)
            normalised = (rgb - mean) / std
            tensor = torch.from_numpy(normalised.transpose(2, 0, 1)).unsqueeze(0)
            with torch.no_grad():
                features = model(tensor)
            return _l2(features.squeeze(0).cpu().numpy())

        model, torch = _clip
        from PIL import Image

        pil = Image.fromarray(cv2.cvtColor(crop, cv2.COLOR_BGR2RGB))
        tensor = _clip_preprocess(pil).unsqueeze(0)
        with torch.no_grad():
            features = model.encode_image(tensor)
        return _l2(features.squeeze(0).cpu().numpy())
    except Exception as exc:  # noqa: BLE001 - a bad crop must not kill the photo
        logger.warning("Appearance embedding failed: %s", exc)
        return None


def crop_person(image: np.ndarray, detection) -> np.ndarray:
    """Full person crop, head included.

    Plan 4.4 is emphatic that the crop must not stop at the neck: hair colour, texture
    and style are a large part of what makes appearance work across a single day.
    """
    x1, y1 = int(max(0, detection.x1)), int(max(0, detection.y1))
    x2, y2 = int(min(image.shape[1], detection.x2)), int(min(image.shape[0], detection.y2))
    if x2 <= x1 or y2 <= y1:
        return np.empty((0, 0, 3), dtype=np.uint8)
    return image[y1:y2, x1:x2]


def crop_head(image: np.ndarray, detection) -> np.ndarray | None:
    """Top slice of the person box, full width -- the dedicated hair/headwear signal.

    Full width rather than the face box because this has to work from behind, where
    there is no face to centre on: braids, locs, a fade, a headwrap, a cap.
    """
    x1, y1 = int(max(0, detection.x1)), int(max(0, detection.y1))
    x2 = int(min(image.shape[1], detection.x2))
    head_height = int(detection.height * config.HEAD_FRACTION)

    if head_height < config.HEAD_MIN_PX or x2 <= x1:
        return None

    y2 = int(min(image.shape[0], y1 + head_height))
    if y2 <= y1:
        return None
    return image[y1:y2, x1:x2]


# --- Sidecar store -----------------------------------------------------------------

def _path_for(event_id: str, detection_id: str, kind: str) -> Path:
    return EMBEDDINGS_ROOT / event_id / detection_id / f"{kind}.npy"


def save(event_id: str, detection_id: str, kind: str, vector: np.ndarray) -> str:
    """Persist one embedding and return the ref string stored in SQL."""
    path = _path_for(event_id, detection_id, kind)
    path.parent.mkdir(parents=True, exist_ok=True)
    np.save(path, vector.astype(np.float32))
    # Ref is relative to EMBEDDINGS_ROOT so the store can be relocated (or swapped for
    # blob storage) without rewriting every row in the database.
    return f"{event_id}/{detection_id}/{kind}.npy"


def load(ref: str | None) -> np.ndarray | None:
    if not ref:
        return None
    path = EMBEDDINGS_ROOT / ref
    if not path.exists():
        logger.warning("Embedding ref %s not found on disk.", ref)
        return None
    try:
        return np.load(path).astype(np.float32)
    except Exception as exc:  # noqa: BLE001
        logger.warning("Could not read embedding %s: %s", ref, exc)
        return None


def available_signals() -> dict[str, bool]:
    """What the service can actually compute right now -- surfaced by /health."""
    return {
        "face": True,  # InsightFace is required; the service does not start without it
        "appearance": _ensure_appearance_backend() is not None,
        "appearance_backend": _appearance_backend or "none",
    }
