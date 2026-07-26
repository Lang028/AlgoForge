"""
Quality, prominence, and the Tier A / Tier B gate (plan 4.3).

This module is the product requirement in code. The definition of the feature working
is: when someone was in focus, or clearly part of a group photo, identify them there --
and do not label every backshot, blurry background figure, or out-of-focus passer-by.
Tier A is that sentence expressed as thresholds.

Tier B detections are stored and may join a cluster as internal evidence, but they never
generate a tag and never appear in an attendee-facing view. The C# side enforces that
too; this is where the decision is made, not where it is guaranteed.
"""

from __future__ import annotations

import cv2
import numpy as np

from . import config
from .detectors import RawDetection


def sharpness_of(image: np.ndarray, detection: RawDetection) -> float:
    """Variance of the Laplacian over the person crop -- the standard focus proxy.

    Low variance means few sharp edges, which at event scale means out of focus or
    motion blurred: the background figures the gate exists to reject.
    """
    x1, y1 = int(max(0, detection.x1)), int(max(0, detection.y1))
    x2, y2 = int(min(image.shape[1], detection.x2)), int(min(image.shape[0], detection.y2))
    if x2 - x1 < 4 or y2 - y1 < 4:
        return 0.0

    crop = image[y1:y2, x1:x2]
    grey = cv2.cvtColor(crop, cv2.COLOR_BGR2GRAY)
    return float(cv2.Laplacian(grey, cv2.CV_64F).var())


def face_min_side(detection: RawDetection) -> float:
    if detection.face is None:
        return 0.0
    fx1, fy1, fx2, fy2 = detection.face
    return float(min(fx2 - fx1, fy2 - fy1))


def prominence_of(
    detection: RawDetection, image_height: int, sharpness: float, min_side: float
) -> float:
    """Continuous 0..1 blend used for ranking, never for gating.

    The gate is a hard pass/fail; this exists so the review UI can pick a cluster's best
    crop and sort members sensibly. Each term is squashed to 0..1 and averaged with the
    weights below -- face size dominates because it is what makes a crop legible as a
    person in a review grid.
    """
    rel_height = detection.height / max(1.0, image_height)

    size_term = min(1.0, rel_height / 0.5)
    face_term = min(1.0, min_side / 160.0)
    sharp_term = min(1.0, sharpness / 300.0)
    # A frontal face reads as more "present" in a photo than a hard profile.
    pose_term = 0.0 if detection.yaw > 180 else max(0.0, 1.0 - (detection.yaw / 90.0))

    return float(
        0.25 * size_term + 0.35 * face_term + 0.25 * sharp_term + 0.15 * pose_term
    )


def is_taggable(detection: RawDetection, image_height: int, sharpness: float, min_side: float) -> bool:
    """Tier A requires every condition below. Any single failure means Tier B.

    Ordered cheapest-first, and deliberately not collapsed into one boolean expression --
    each clause is a separate product rule and reads better as one.
    """
    # No face, no tag. An identity is never asserted from appearance alone: that is both
    # the correctness rule and the consent posture (plan 11).
    if detection.face is None:
        return False

    if detection.face_score < config.GATE_FACE_SCORE:
        return False

    # Backshots and hard profiles are Tier B by definition -- the product requirement,
    # not a tuning choice. Someone who turned away from the camera all day stays unseen.
    if detection.yaw > config.GATE_MAX_YAW:
        return False

    if min_side < config.GATE_FACE_MIN_SIDE:
        return False

    # Blur is the background/out-of-focus signal.
    if sharpness < config.S_MIN:
        return False

    # Either the person fills a reasonable slice of frame, or their face is big enough to
    # be unambiguous. The OR is what keeps group photos working: everyone in a row of
    # twelve has a small rel_height, but their faces are still sharp and sizeable.
    rel_height = detection.height / max(1.0, image_height)
    return rel_height >= config.GATE_REL_HEIGHT or min_side >= config.GATE_FACE_SIDE_ALT


def score(image: np.ndarray, detection: RawDetection) -> dict:
    """Compute every quality signal for one detection in a single pass."""
    image_height = image.shape[0]
    sharpness = sharpness_of(image, detection)
    min_side = face_min_side(detection)

    return {
        "sharpness": sharpness,
        "face_min_side": min_side,
        "prominence": prominence_of(detection, image_height, sharpness, min_side),
        "is_taggable": is_taggable(detection, image_height, sharpness, min_side),
    }
