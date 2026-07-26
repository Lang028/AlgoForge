"""
Clustering invariants, exercised with synthetic embeddings.

No models are loaded here: embeddings are hand-built vectors, so every assertion is
about the fusion, veto and gate logic rather than about InsightFace or OSNet. That makes
this suite the safety net for the section 9 calibration work -- thresholds can be swept
freely, but if one of these fails, the change broke a rule the product depends on rather
than merely retuning it.

    cd face_service && python -m pytest tests -q
"""

from __future__ import annotations

import os
import tempfile

import numpy as np
import pytest

os.environ.setdefault("GEEKEDON_EMBEDDINGS_DIR", tempfile.mkdtemp(prefix="geekedon-test-"))

from pipeline import embeddings  # noqa: E402
from pipeline.clustering import CANNOT_LINK, Candidate, cluster_event, similarity  # noqa: E402
from pipeline.schemas import ClusterInput, Constraint  # noqa: E402

RNG = np.random.default_rng(42)
EVENT = "test-event"


def _unit(vector: np.ndarray) -> np.ndarray:
    return (vector / np.linalg.norm(vector)).astype(np.float32)


def _identity(seed: int, jitter: float = 0.0, dim: int = 512) -> np.ndarray:
    """A stable per-seed vector, optionally perturbed to simulate another photo."""
    base = np.random.default_rng(seed).normal(size=dim)
    if jitter:
        base = base + RNG.normal(size=dim) * jitter
    return _unit(base)


def detection(
    det_id: str,
    photo_id: str,
    *,
    face_seed: int | None = None,
    face_q: float = 0.0,
    taggable: bool = False,
    app_seed: int | None = None,
    head_seed: int | None = None,
    jitter: float = 0.05,
) -> ClusterInput:
    refs: dict[str, str | None] = {"face": None, "app": None, "head": None}
    for kind, seed in (("face", face_seed), ("app", app_seed), ("head", head_seed)):
        if seed is not None:
            refs[kind] = embeddings.save(EVENT, det_id, kind, _identity(seed, jitter))

    return ClusterInput(
        id=det_id,
        photo_id=photo_id,
        face_quality=face_q,
        is_taggable=taggable,
        face_embedding_ref=refs["face"],
        appearance_embedding_ref=refs["app"],
        head_embedding_ref=refs["head"],
    )


def group_of(clusters, det_id: str) -> int | None:
    for index, cluster in enumerate(clusters):
        if any(m.detection_id == det_id for m in cluster.members):
            return index
    return None


def test_same_person_clusters_and_others_stay_separate():
    dets = [
        detection("a1", "p1", face_seed=1, face_q=0.9, taggable=True),
        detection("a2", "p2", face_seed=1, face_q=0.9, taggable=True),
        detection("a3", "p3", face_seed=1, face_q=0.9, taggable=True),
        detection("b1", "p1", face_seed=2, face_q=0.9, taggable=True),
        detection("b2", "p2", face_seed=2, face_q=0.9, taggable=True),
    ]
    clusters, _ = cluster_event(dets)

    assert group_of(clusters, "a1") == group_of(clusters, "a2") == group_of(clusters, "a3")
    assert group_of(clusters, "b1") == group_of(clusters, "b2")
    assert group_of(clusters, "a1") != group_of(clusters, "b1")


def test_two_boxes_in_one_photo_never_share_a_cluster():
    """The one rule no signal may override: one photo, two boxes, two people."""
    dets = [
        detection("x1", "p9", face_seed=3, face_q=0.9, taggable=True, jitter=0.01),
        detection("x2", "p9", face_seed=3, face_q=0.9, taggable=True, jitter=0.01),
    ]
    clusters, _ = cluster_event(dets)

    assert group_of(clusters, "x1") != group_of(clusters, "x2")


def test_appearance_cannot_override_two_confident_disagreeing_faces():
    """The uniform / dress-code collision, which is the whole reason the veto exists."""
    first = detection("u1", "p1", face_seed=10, face_q=0.95, taggable=True, jitter=0.01)
    second = detection("u2", "p2", face_seed=20, face_q=0.95, taggable=True, jitter=0.01)

    # Byte-identical outfit and hair for two different people.
    uniform = _identity(99)
    for det_id, det in (("u1", first), ("u2", second)):
        det.appearance_embedding_ref = embeddings.save(EVENT, det_id, "app", uniform)
        det.head_embedding_ref = embeddings.save(EVENT, det_id, "head", uniform)

    assert similarity(Candidate.from_input(first), Candidate.from_input(second)) == CANNOT_LINK

    clusters, _ = cluster_event([first, second])
    assert group_of(clusters, "u1") != group_of(clusters, "u2")


def test_faceless_detections_never_form_a_surfaced_cluster():
    """Plan 5.2 / 11: a person who avoided the camera is never surfaced by their outfit."""
    dets = [
        detection("t1", "p1", app_seed=5, head_seed=5, jitter=0.02),
        detection("t2", "p2", app_seed=5, head_seed=5, jitter=0.02),
    ]
    clusters, unclustered = cluster_event(dets)

    assert clusters == []
    assert set(unclustered) == {"t1", "t2"}


def test_appearance_rescues_a_backshot_into_a_face_anchored_cluster():
    """The recall win the fusion design exists for."""
    dets = [
        detection("m1", "p1", face_seed=7, face_q=0.9, taggable=True, app_seed=7, head_seed=7, jitter=0.03),
        detection("m2", "p2", face_seed=7, face_q=0.9, taggable=True, app_seed=7, head_seed=7, jitter=0.03),
        detection("m3", "p3", app_seed=7, head_seed=7, jitter=0.03),  # no face
    ]
    clusters, _ = cluster_event(dets)
    index = group_of(clusters, "m1")

    assert group_of(clusters, "m3") == index

    cluster = clusters[index]
    assert cluster.has_taggable
    assert cluster.anchor_detection_id in {"m1", "m2"}
    confidences = [m.confidence for m in cluster.members]
    assert confidences == sorted(confidences), "review UI relies on shakiest-first order"


def test_unrelated_appearance_is_not_attached():
    dets = [
        detection("s1", "p1", face_seed=11, face_q=0.9, taggable=True, app_seed=11, head_seed=11, jitter=0.03),
        detection("s2", "p2", face_seed=11, face_q=0.9, taggable=True, app_seed=11, head_seed=11, jitter=0.03),
        detection("s3", "p3", app_seed=77, head_seed=77, jitter=0.03),
    ]
    _, unclustered = cluster_event(dets)

    assert "s3" in unclustered


@pytest.mark.parametrize(
    "constraint_type,expect_same",
    [("cannot_link", False), ("must_link", True)],
)
def test_photographer_constraints_are_honoured(constraint_type: str, expect_same: bool):
    """Corrections must survive a recluster, or the photographer's work evaporates."""
    # Same face for cannot_link (would otherwise merge), different for must_link
    # (would otherwise stay apart) -- each case fights the model's default answer.
    seeds = (30, 30) if constraint_type == "cannot_link" else (40, 41)
    dets = [
        detection("c1", "p1", face_seed=seeds[0], face_q=0.9, taggable=True),
        detection("c2", "p2", face_seed=seeds[1], face_q=0.9, taggable=True),
    ]
    clusters, _ = cluster_event(dets, [Constraint(a="c1", b="c2", type=constraint_type)])

    assert (group_of(clusters, "c1") == group_of(clusters, "c2")) is expect_same


def test_missing_signals_degrade_rather_than_break():
    """Face-only, i.e. neither YOLO nor an appearance backend installed."""
    dets = [
        detection("f1", "p1", face_seed=50, face_q=0.9, taggable=True),
        detection("f2", "p2", face_seed=50, face_q=0.9, taggable=True),
    ]
    clusters, _ = cluster_event(dets)

    assert group_of(clusters, "f1") == group_of(clusters, "f2")
