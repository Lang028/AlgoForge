"""
Wire contract between the Python pipeline and the C# app.

Field names are snake_case here and mapped explicitly with [JsonPropertyName] on the
C# side, so neither language has to adopt the other's casing convention.
"""

from pydantic import BaseModel, Field


class Box(BaseModel):
    """Relative coordinates, 0..1 of image width/height.

    Plan 4.1: relative rather than pixel coordinates so boxes survive a rendition
    change -- the same numbers draw correctly over the thumbnail, the display copy and
    the original.
    """

    x: float
    y: float
    w: float
    h: float


class DetectionResult(BaseModel):
    # Minted here, not by the database. D14: the worker mints IDs client-side without a
    # round trip, and the embedding files are written under this id before the C# side
    # has ever seen the detection.
    id: str
    box: Box
    face_box: Box | None = None

    face_quality: float = 0.0  # 0 means no usable face was found
    sharpness: float = 0.0
    prominence: float = 0.0
    is_taggable: bool = False

    face_embedding_ref: str | None = None
    appearance_embedding_ref: str | None = None
    head_embedding_ref: str | None = None


class DetectResponse(BaseModel):
    photo_id: str
    detections: list[DetectionResult] = Field(default_factory=list)
    # Which optional models were actually available for this run. The C# side logs it;
    # it is the difference between "no appearance signal because the crop was bad" and
    # "no appearance signal because torchreid isn't installed".
    #
    # Mixed value types on purpose: the per-signal flags are booleans, but
    # appearance_backend names which of OSNet/open_clip actually answered.
    signals: dict[str, bool | str] = Field(default_factory=dict)


class ClusterInput(BaseModel):
    """One detection as clustering sees it -- metadata only.

    Embeddings are deliberately absent: they are loaded from the sidecar store by ref
    (D15, storage owned by the Python service). Sending a few thousand 512-float
    vectors over HTTP on every recluster would be the slowest part of the pipeline.
    """

    id: str
    photo_id: str
    face_quality: float = 0.0
    is_taggable: bool = False
    face_embedding_ref: str | None = None
    appearance_embedding_ref: str | None = None
    head_embedding_ref: str | None = None


class Constraint(BaseModel):
    """A photographer correction, replayed as a hard constraint.

    Not used until M4, but the field exists in the request now so adding constraint
    persistence later does not change the wire format.
    """

    a: str
    b: str
    type: str  # "must_link" | "cannot_link"


class ClusterRequest(BaseModel):
    event_id: str
    detections: list[ClusterInput] = Field(default_factory=list)
    constraints: list[Constraint] = Field(default_factory=list)


class ClusterMember(BaseModel):
    detection_id: str
    # Fused similarity that earned this member its place. Anchors are 1.0. The review UI
    # sorts by this so the photographer checks the shakiest attachments first.
    confidence: float


class ClusterResult(BaseModel):
    members: list[ClusterMember]
    # Highest-quality face in the cluster -- the review UI's cover crop.
    anchor_detection_id: str | None = None
    # False for clusters made purely of Tier B detections. Plan 5.2: these are never
    # shown to anyone, so the C# side keeps them out of every surfaced query.
    has_taggable: bool = False


class ClusterResponse(BaseModel):
    event_id: str
    clusters: list[ClusterResult] = Field(default_factory=list)
    unclustered: list[str] = Field(default_factory=list)
