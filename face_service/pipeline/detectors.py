"""
Person and face detection, and the association between them (plan 4.1 / 4.2).

Model loading is lazy and failure-tolerant. A missing optional model degrades the
pipeline to fewer signals rather than taking the service down -- the C# side can still
store detections, and the /health endpoint reports which signals are live. The one
genuinely required model is InsightFace: without a face there is no Tier A, and without
Tier A nothing is ever taggable, so a face-less pipeline has no product left.
"""

from __future__ import annotations

import logging
import os
from dataclasses import dataclass, field

import numpy as np

from . import config

logger = logging.getLogger(__name__)

_face_app = None
_yolo = None
_yolo_failed = False


@dataclass
class RawDetection:
    """One person in one photo, before quality scoring and embedding."""

    # Pixel coordinates on the working image.
    x1: float
    y1: float
    x2: float
    y2: float

    face: tuple[float, float, float, float] | None = None
    face_score: float = 0.0
    yaw: float = 999.0  # 999 = unknown, i.e. no face. Fails the yaw gate by default.
    # InsightFace's aligned-and-normalised embedding, carried through so the face is
    # never embedded twice.
    face_embedding: np.ndarray | None = None
    # True when no YOLO person box contained this face and the box was synthesised by
    # expanding the face box (plan 4.2 -- happens on tight head crops).
    synthesised_body: bool = False

    extras: dict = field(default_factory=dict)

    @property
    def width(self) -> float:
        return max(0.0, self.x2 - self.x1)

    @property
    def height(self) -> float:
        return max(0.0, self.y2 - self.y1)


def get_face_app():
    """InsightFace buffalo_l: SCRFD detector + ArcFace recogniser + pose."""
    global _face_app
    if _face_app is None:
        import insightface

        _face_app = insightface.app.FaceAnalysis(
            name="buffalo_l", providers=["CPUExecutionProvider"]
        )
        _face_app.prepare(ctx_id=0, det_size=(640, 640))
    return _face_app


def get_yolo():
    """Ultralytics YOLO11s for person boxes. Optional.

    Without it every detection is face-derived: a synthesised body box around each face.
    That still clusters (face + a rough head crop) but loses the appearance signal's
    value, since the crop no longer covers the outfit. Degraded, not broken.
    """
    global _yolo, _yolo_failed
    if _yolo is not None or _yolo_failed:
        return _yolo
    try:
        cache_root = config.CACHE_ROOT
        cache_root.mkdir(parents=True, exist_ok=True)
        os.environ.setdefault("YOLO_CONFIG_DIR", str(cache_root / "ultralytics"))
        os.environ.setdefault("MPLCONFIGDIR", str(cache_root / "matplotlib"))

        from ultralytics import YOLO

        _yolo = YOLO(config.YOLO_WEIGHTS)
    except Exception as exc:  # noqa: BLE001 - any import/download failure degrades alike
        _yolo_failed = True
        logger.warning(
            "YOLO unavailable (%s). Falling back to face-derived person boxes; "
            "appearance embeddings will cover only the head-and-shoulders region.",
            exc,
        )
    return _yolo


def detect_persons(image: np.ndarray) -> list[tuple[float, float, float, float]]:
    model = get_yolo()
    if model is None:
        return []

    results = model.predict(
        image, classes=[config.YOLO_PERSON_CLASS], conf=config.PERSON_CONF, verbose=False
    )
    boxes: list[tuple[float, float, float, float]] = []
    for result in results:
        if result.boxes is None:
            continue
        for xyxy in result.boxes.xyxy.cpu().numpy():
            x1, y1, x2, y2 = (float(v) for v in xyxy[:4])
            boxes.append((x1, y1, x2, y2))
    return boxes


def _containment(face: tuple[float, float, float, float], person: tuple[float, float, float, float]) -> float:
    """Fraction of the face box's area that falls inside the person box."""
    fx1, fy1, fx2, fy2 = face
    px1, py1, px2, py2 = person

    overlap_w = max(0.0, min(fx2, px2) - max(fx1, px1))
    overlap_h = max(0.0, min(fy2, py2) - max(fy1, py1))
    face_area = max(1e-6, (fx2 - fx1) * (fy2 - fy1))
    return (overlap_w * overlap_h) / face_area


def _yaw_of(face) -> float:
    """Absolute yaw in degrees, or 999 when pose is unavailable.

    999 rather than 0 deliberately: unknown pose must fail the yaw gate, not sail
    through it as if the subject were perfectly frontal.
    """
    pose = getattr(face, "pose", None)
    if pose is None or len(pose) < 2:
        return 999.0
    return abs(float(pose[1]))


def detect(image: np.ndarray) -> list[RawDetection]:
    """Detect people and faces, and pair them up.

    Returns one RawDetection per person. Faces that land inside no person box become
    their own detection with a synthesised body box; person boxes with no face are kept
    too -- they are Tier B by definition but still carry appearance evidence.
    """
    height, width = image.shape[:2]
    person_boxes = detect_persons(image)
    faces = get_face_app().get(image)

    detections: list[RawDetection] = []
    # Index into person_boxes -> the detection built from it, so a second face landing in
    # the same person box does not silently overwrite the first.
    claimed: dict[int, RawDetection] = {}

    for face in faces:
        fx1, fy1, fx2, fy2 = (float(v) for v in face.bbox[:4])
        face_box = (
            max(0.0, fx1),
            max(0.0, fy1),
            min(float(width), fx2),
            min(float(height), fy2),
        )

        best_index, best_ratio = None, config.FACE_CONTAINMENT
        for index, person in enumerate(person_boxes):
            ratio = _containment(face_box, person)
            if ratio >= best_ratio and index not in claimed:
                best_index, best_ratio = index, ratio

        embedding = getattr(face, "normed_embedding", None)
        score = float(getattr(face, "det_score", 0.0))
        yaw = _yaw_of(face)

        if best_index is None:
            # No person box owns this face. Synthesise one so downstream code always has
            # a body region to crop: roughly head-to-chest, which is all that can be
            # inferred from a face box alone.
            fw, fh = face_box[2] - face_box[0], face_box[3] - face_box[1]
            body = (
                max(0.0, face_box[0] - fw * 0.5),
                max(0.0, face_box[1] - fh * 0.4),
                min(float(width), face_box[2] + fw * 0.5),
                min(float(height), face_box[3] + fh * 2.5),
            )
            detections.append(
                RawDetection(
                    x1=body[0],
                    y1=body[1],
                    x2=body[2],
                    y2=body[3],
                    face=face_box,
                    face_score=score,
                    yaw=yaw,
                    face_embedding=embedding,
                    synthesised_body=True,
                )
            )
            continue

        person = person_boxes[best_index]
        detection = RawDetection(
            x1=person[0],
            y1=person[1],
            x2=person[2],
            y2=person[3],
            face=face_box,
            face_score=score,
            yaw=yaw,
            face_embedding=embedding,
        )
        claimed[best_index] = detection
        detections.append(detection)

    # Person boxes nobody's face claimed: backshots, occluded faces, figures in the
    # background. Tier B on arrival, but their appearance and head embeddings are real
    # evidence -- this is exactly the recall the whole fusion idea exists to recover.
    for index, person in enumerate(person_boxes):
        if index in claimed:
            continue
        detections.append(RawDetection(x1=person[0], y1=person[1], x2=person[2], y2=person[3]))

    return detections
