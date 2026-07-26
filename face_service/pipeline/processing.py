"""
Per-photo processing: detect, gate, embed, persist embeddings (plan 4).
"""

from __future__ import annotations

import logging
import uuid

import cv2
import numpy as np

from . import config, detectors, embeddings, quality
from .schemas import Box, DetectionResult

logger = logging.getLogger(__name__)


def decode(contents: bytes) -> np.ndarray | None:
    image = cv2.imdecode(np.frombuffer(contents, dtype=np.uint8), cv2.IMREAD_COLOR)
    if image is None:
        return None
    return _to_working_size(image)


def _to_working_size(image: np.ndarray) -> np.ndarray:
    """Downscale oversized images to a fixed working resolution.

    Every pixel threshold in the gate (FACE_MIN_SIDE, S_MIN) is only meaningful at a
    known scale. Without this, the same face would pass the gate on a 45MP original and
    fail it on a phone photo of the same scene.
    """
    height, width = image.shape[:2]
    long_side = max(height, width)
    if long_side <= config.WORKING_LONG_SIDE:
        return image

    scale = config.WORKING_LONG_SIDE / long_side
    return cv2.resize(
        image, (int(width * scale), int(height * scale)), interpolation=cv2.INTER_AREA
    )


def process_photo(image: np.ndarray, event_id: str, photo_id: str) -> list[DetectionResult]:
    height, width = image.shape[:2]
    raw_detections = detectors.detect(image)
    results: list[DetectionResult] = []

    for raw in raw_detections:
        detection_id = str(uuid.uuid4())
        scores = quality.score(image, raw)

        face_ref = None
        # Embedded at a lower bar than the gate: a Tier B face still carries real
        # evidence for clustering, it just can never be tagged.
        if raw.face_embedding is not None and raw.face_score >= config.EMBED_FACE_SCORE:
            face_ref = embeddings.save(
                event_id, detection_id, "face", np.asarray(raw.face_embedding)
            )

        appearance_ref = None
        person_crop = embeddings.crop_person(image, raw)
        if person_crop.size > 0:
            vector = embeddings.embed_appearance(person_crop)
            if vector is not None:
                appearance_ref = embeddings.save(event_id, detection_id, "app", vector)

        head_ref = None
        head_crop = embeddings.crop_head(image, raw)
        if head_crop is not None and head_crop.size > 0:
            vector = embeddings.embed_appearance(head_crop)
            if vector is not None:
                head_ref = embeddings.save(event_id, detection_id, "head", vector)

        results.append(
            DetectionResult(
                id=detection_id,
                box=Box(
                    x=raw.x1 / width,
                    y=raw.y1 / height,
                    w=raw.width / width,
                    h=raw.height / height,
                ),
                face_box=(
                    Box(
                        x=raw.face[0] / width,
                        y=raw.face[1] / height,
                        w=(raw.face[2] - raw.face[0]) / width,
                        h=(raw.face[3] - raw.face[1]) / height,
                    )
                    if raw.face is not None
                    else None
                ),
                # Face quality is the detector's own score. It drives the anchor
                # threshold and the veto, so it stays the raw signal rather than a blend.
                face_quality=float(raw.face_score),
                sharpness=scores["sharpness"],
                prominence=scores["prominence"],
                is_taggable=scores["is_taggable"],
                face_embedding_ref=face_ref,
                appearance_embedding_ref=appearance_ref,
                head_embedding_ref=head_ref,
            )
        )

    return results
