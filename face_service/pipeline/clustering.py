"""
Fused pairwise similarity and two-phase clustering (plan 5).

The governing bias is precision over recall on merges. Wrongly merging two people is the
damaging error -- it suggests someone else's tags to an attendee, which is a consent
failure, not just a bug. Fragmenting one person into two clusters is cheap, because the
photographer has a merge tool. Every threshold here leans that way.
"""

from __future__ import annotations

import logging
from dataclasses import dataclass, field

import numpy as np

from . import config, embeddings
from .schemas import ClusterInput, ClusterMember, ClusterResult, Constraint

logger = logging.getLogger(__name__)

CANNOT_LINK = -1.0


@dataclass
class Candidate:
    """A detection with its embeddings resolved, ready to compare."""

    id: str
    photo_id: str
    face_quality: float
    is_taggable: bool
    face: np.ndarray | None = None
    appearance: np.ndarray | None = None
    head: np.ndarray | None = None

    @classmethod
    def from_input(cls, item: ClusterInput) -> "Candidate":
        return cls(
            id=item.id,
            photo_id=item.photo_id,
            face_quality=item.face_quality,
            is_taggable=item.is_taggable,
            face=embeddings.load(item.face_embedding_ref),
            appearance=embeddings.load(item.appearance_embedding_ref),
            head=embeddings.load(item.head_embedding_ref),
        )


def cosine(a: np.ndarray | None, b: np.ndarray | None) -> float | None:
    if a is None or b is None or a.shape != b.shape or a.size == 0:
        return None
    denominator = float(np.linalg.norm(a) * np.linalg.norm(b))
    if denominator < 1e-8:
        return None
    return float(np.dot(a, b) / denominator)


def similarity(a: Candidate, b: Candidate) -> float:
    """Quality-weighted fusion of whichever signals both detections have.

    Returns CANNOT_LINK for a hard veto, otherwise a weighted average in roughly -1..1.

    The face weight scales with the pair's *weaker* face quality, which is the mechanism
    that makes the two signals co-equal in practice rather than by assertion: a sharp
    frontal pair ends up face-dominated, a soft profile pair ends up appearance- and
    hair-dominated, and in between both genuinely vote.
    """
    # Two boxes in one photo are two different people. No signal can argue with this.
    if a.photo_id == b.photo_id:
        return CANNOT_LINK

    # Two confident faces that clearly disagree: appearance can NEVER override this.
    # Uniforms, dress codes and same-hair collisions are exactly the case where the
    # appearance signal is confidently wrong, so the face gets an absolute veto.
    if a.face_quality >= config.FACE_VETO_QUALITY and b.face_quality >= config.FACE_VETO_QUALITY:
        face_similarity = cosine(a.face, b.face)
        if face_similarity is not None and face_similarity < config.FACE_VETO_SIM:
            return CANNOT_LINK

    sims: list[float] = []
    weights: list[float] = []

    face_similarity = cosine(a.face, b.face)
    if face_similarity is not None:
        sims.append(face_similarity)
        weights.append(config.W_FACE * min(a.face_quality, b.face_quality))

    appearance_similarity = cosine(a.appearance, b.appearance)
    if appearance_similarity is not None:
        sims.append(appearance_similarity)
        weights.append(config.W_APPEARANCE)

    head_similarity = cosine(a.head, b.head)
    if head_similarity is not None:
        sims.append(head_similarity)
        weights.append(config.W_HEAD)

    total = sum(weights)
    if not sims or total <= 0:
        return 0.0

    # Dividing by the summed weight is what lets a missing signal degrade gracefully:
    # the remaining signals renormalise instead of the score collapsing toward zero.
    return float(sum(s * w for s, w in zip(sims, weights)) / total)


class _Components:
    """Union-find that refuses any merge putting two same-photo detections together.

    Enforcing cannot-link during the merge, rather than checking afterwards, is what
    keeps the constraint true transitively -- plain connected components would happily
    chain A-B-C into one group where A and C are two people standing side by side.
    """

    def __init__(self, candidates: list[Candidate]):
        self._parent = {c.id: c.id for c in candidates}
        self._photos = {c.id: {c.photo_id} for c in candidates}
        self._forbidden: dict[str, set[str]] = {c.id: set() for c in candidates}

    def find(self, item: str) -> str:
        while self._parent[item] != item:
            self._parent[item] = self._parent[self._parent[item]]
            item = self._parent[item]
        return item

    def forbid(self, a: str, b: str) -> None:
        """Record an explicit cannot-link between two detections' current components."""
        root_a, root_b = self.find(a), self.find(b)
        self._forbidden[root_a].add(root_b)
        self._forbidden[root_b].add(root_a)

    def can_merge(self, a: str, b: str) -> bool:
        root_a, root_b = self.find(a), self.find(b)
        if root_a == root_b:
            return False
        if root_b in self._forbidden[root_a]:
            return False
        return self._photos[root_a].isdisjoint(self._photos[root_b])

    def merge(self, a: str, b: str) -> bool:
        if not self.can_merge(a, b):
            return False
        root_a, root_b = self.find(a), self.find(b)
        self._parent[root_b] = root_a
        self._photos[root_a] |= self._photos[root_b]
        self._forbidden[root_a] |= self._forbidden[root_b]
        return True

    def groups(self) -> dict[str, list[str]]:
        out: dict[str, list[str]] = {}
        for item in self._parent:
            out.setdefault(self.find(item), []).append(item)
        return out


@dataclass
class _Cluster:
    members: dict[str, float] = field(default_factory=dict)  # detection id -> confidence
    photos: set[str] = field(default_factory=set)


def _anchor_phase(candidates: list[Candidate], constraints: list[Constraint]) -> list[_Cluster]:
    """Phase 1: high-precision anchors from confident Tier A faces only (plan 5.2).

    Face-only and face-quality-gated on purpose. These clusters are the identity spine
    everything else attaches to, so they are built from the single most reliable signal
    and nothing else. Appearance never mints an identity.
    """
    anchors = [
        c
        for c in candidates
        if c.is_taggable and c.face is not None and c.face_quality >= config.ANCHOR_MIN_QUALITY
    ]
    components = _Components(anchors)
    anchor_ids = {c.id for c in anchors}

    for constraint in constraints:
        if constraint.type == "cannot_link" and constraint.a in anchor_ids and constraint.b in anchor_ids:
            components.forbid(constraint.a, constraint.b)

    # Collect every qualifying edge, then merge strongest-first. Order matters: when a
    # merge has to be refused for a photo conflict, the pair we keep should be the more
    # confident one.
    edges: list[tuple[float, str, str]] = []
    for i in range(len(anchors)):
        for j in range(i + 1, len(anchors)):
            face_similarity = cosine(anchors[i].face, anchors[j].face)
            if (
                face_similarity is not None
                and face_similarity >= config.FACE_ANCHOR_EDGE
                and anchors[i].photo_id != anchors[j].photo_id
            ):
                edges.append((face_similarity, anchors[i].id, anchors[j].id))

    edges.sort(key=lambda e: e[0], reverse=True)
    for _, a, b in edges:
        components.merge(a, b)

    # Photographer must-links are applied after the automatic edges so a correction can
    # join two groups the model kept apart.
    for constraint in constraints:
        if constraint.type == "must_link" and constraint.a in anchor_ids and constraint.b in anchor_ids:
            components.merge(constraint.a, constraint.b)

    by_id = {c.id: c for c in anchors}
    clusters: list[_Cluster] = []
    for member_ids in components.groups().values():
        cluster = _Cluster()
        for member_id in member_ids:
            # Anchors are definitionally certain within this phase.
            cluster.members[member_id] = 1.0
            cluster.photos.add(by_id[member_id].photo_id)
        clusters.append(cluster)

    return clusters


def _attach_phase(
    candidates: list[Candidate],
    clusters: list[_Cluster],
    constraints: list[Constraint],
) -> list[str]:
    """Phase 2: attach everything else by fused similarity (plan 5.2).

    Processed in descending face quality so the most trustworthy leftovers claim their
    cluster before the weak evidence does. Returns the ids that stayed unclustered.
    """
    by_id = {c.id: c for c in candidates}
    assigned = {member for cluster in clusters for member in cluster.members}

    forbidden: dict[str, set[str]] = {}
    for constraint in constraints:
        if constraint.type == "cannot_link":
            forbidden.setdefault(constraint.a, set()).add(constraint.b)
            forbidden.setdefault(constraint.b, set()).add(constraint.a)

    remaining = sorted(
        (c for c in candidates if c.id not in assigned),
        key=lambda c: (c.is_taggable, c.face_quality),
        reverse=True,
    )

    unclustered: list[str] = []
    for candidate in remaining:
        best_cluster, best_score = None, config.FUSED_ATTACH

        for cluster in clusters:
            # Cluster-level cannot-link: the person is already in this cluster via
            # another photo, so a second box from the same photo must be someone else.
            if candidate.photo_id in cluster.photos:
                continue
            if any(member in forbidden.get(candidate.id, ()) for member in cluster.members):
                continue

            scores = []
            vetoed = False
            for member_id in cluster.members:
                pair_score = similarity(candidate, by_id[member_id])
                if pair_score == CANNOT_LINK:
                    # One confident disagreement disqualifies the whole cluster: the
                    # veto is absolute, so an average must never be able to dilute it.
                    vetoed = True
                    break
                scores.append(pair_score)

            if vetoed or not scores:
                continue

            # Mean of the top-K rather than the single best member, so one outlier
            # member cannot drag a stranger in behind it.
            scores.sort(reverse=True)
            fused = float(np.mean(scores[: config.ATTACH_TOP_K]))

            if fused >= best_score:
                best_cluster, best_score = cluster, fused

        if best_cluster is None:
            unclustered.append(candidate.id)
            continue

        best_cluster.members[candidate.id] = best_score
        best_cluster.photos.add(candidate.photo_id)

    return unclustered


def cluster_event(
    detections: list[ClusterInput], constraints: list[Constraint] | None = None
) -> tuple[list[ClusterResult], list[str]]:
    """Run the full two-phase clustering for one event.

    Reclusters from scratch every time. Plan 5 is explicit that this is the right call at
    one-day-event scale (hundreds of photos, low thousands of detections) and that
    incremental clustering should not be built -- keeping cluster identity stable is the
    C# side's job, by reconciling these groups onto existing rows.
    """
    constraints = constraints or []
    candidates = [Candidate.from_input(item) for item in detections]

    clusters = _anchor_phase(candidates, constraints)
    unclustered = _attach_phase(candidates, clusters, constraints)

    by_id = {c.id: c for c in candidates}
    results: list[ClusterResult] = []

    for cluster in clusters:
        members = [
            ClusterMember(detection_id=member_id, confidence=confidence)
            for member_id, confidence in cluster.members.items()
        ]
        # Shakiest first: the review UI wants the photographer's attention on the
        # attachments most likely to be wrong.
        members.sort(key=lambda m: m.confidence)

        taggable = [m for m in members if by_id[m.detection_id].is_taggable]
        anchor = max(
            taggable,
            key=lambda m: by_id[m.detection_id].face_quality,
            default=None,
        )

        results.append(
            ClusterResult(
                members=members,
                anchor_detection_id=anchor.detection_id if anchor else None,
                # A cluster with no Tier A member is never surfaced to anyone -- the
                # person who avoided the camera, venue staff shot from behind.
                has_taggable=bool(taggable),
            )
        )

    # Biggest first: the review grid leads with the people who appear most.
    results.sort(key=lambda r: len(r.members), reverse=True)
    return results, unclustered
