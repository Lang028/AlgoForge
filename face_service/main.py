"""
Person identification service for AlgoForge / Geeked On.

Two responsibilities, one per endpoint:

  POST /detect   one photo in, detections out (boxes, gate decision, embedding refs)
  POST /cluster  an event's detection metadata in, cluster assignments out

Persistence stays on the C# side; this service owns only the embedding sidecar store
(D15). It holds no database connection and no knowledge of attendees, tags or consent --
it reports what it sees, and the web app decides what that is allowed to mean.

Run locally (D19 -- the worker runs on a team laptop):
    uvicorn main:app --port 8000
"""

import logging

from fastapi import FastAPI, File, Form, HTTPException, UploadFile

from pipeline import detectors, embeddings, processing
from pipeline.clustering import cluster_event
from pipeline.schemas import ClusterRequest, ClusterResponse, DetectResponse

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

app = FastAPI(title="AlgoForge Person Identification Service")


@app.on_event("startup")
def warm_up() -> None:
    """Load models once at startup rather than on the first request.

    Otherwise the first photo of an upload batch eats a ~10s model-load penalty, and
    with synchronous processing that lands directly in a photographer's request.
    """
    detectors.get_face_app()
    detectors.get_yolo()
    embeddings.available_signals()


@app.get("/health")
def health() -> dict:
    """Reports which signals are actually live.

    Worth checking before a calibration run or a demo: a missing appearance backend is
    silent at the API level but materially changes clustering quality, and this is the
    difference between diagnosing that in ten seconds and losing an afternoon.
    """
    return {"status": "ok", "signals": embeddings.available_signals()}


@app.post("/detect", response_model=DetectResponse)
async def detect(
    file: UploadFile = File(...),
    photo_id: str = Form(...),
    event_id: str = Form(...),
) -> DetectResponse:
    contents = await file.read()
    image = processing.decode(contents)
    if image is None:
        raise HTTPException(status_code=400, detail="Could not decode image")

    detections = processing.process_photo(image, event_id=event_id, photo_id=photo_id)
    logger.info(
        "photo %s: %d detections, %d taggable",
        photo_id,
        len(detections),
        sum(1 for d in detections if d.is_taggable),
    )

    return DetectResponse(
        photo_id=photo_id,
        detections=detections,
        signals=embeddings.available_signals(),
    )


@app.post("/cluster", response_model=ClusterResponse)
def cluster(request: ClusterRequest) -> ClusterResponse:
    clusters, unclustered = cluster_event(request.detections, request.constraints)
    logger.info(
        "event %s: %d detections -> %d clusters (%d surfaced), %d unclustered",
        request.event_id,
        len(request.detections),
        len(clusters),
        sum(1 for c in clusters if c.has_taggable),
        len(unclustered),
    )

    return ClusterResponse(
        event_id=request.event_id, clusters=clusters, unclustered=unclustered
    )
