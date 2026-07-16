"""
Face detection service for AlgoForge / Geeked On.

Single responsibility: given a photo, find faces and return each one's
normalized bounding box + confidence + embedding. No clustering, no
persistence, no database access -- that logic lives in the C# app
(FaceMatchingService). This keeps the Python side stateless and simple
to run locally (D19: worker runs on a team laptop).
"""

from fastapi import FastAPI, UploadFile, File, HTTPException
from pydantic import BaseModel
import numpy as np
import cv2
import insightface

app = FastAPI(title="AlgoForge Face Detection Service")

_face_app: insightface.app.FaceAnalysis | None = None


def get_face_app() -> insightface.app.FaceAnalysis:
    global _face_app
    if _face_app is None:
        _face_app = insightface.app.FaceAnalysis(name="buffalo_l", providers=["CPUExecutionProvider"])
        _face_app.prepare(ctx_id=0, det_size=(640, 640))
    return _face_app


class DetectedFace(BaseModel):
    box_x: float
    box_y: float
    box_width: float
    box_height: float
    confidence: float
    embedding: list[float]


class DetectResponse(BaseModel):
    faces: list[DetectedFace]


@app.on_event("startup")
def warm_up_model():
    # Load the model once at startup rather than on the first request, so the first
    # photo upload isn't the one that eats a ~10s model-load penalty.
    get_face_app()


@app.get("/health")
def health():
    return {"status": "ok"}


@app.post("/detect", response_model=DetectResponse)
async def detect(file: UploadFile = File(...)):
    contents = await file.read()
    image_array = np.frombuffer(contents, dtype=np.uint8)
    image = cv2.imdecode(image_array, cv2.IMREAD_COLOR)

    if image is None:
        raise HTTPException(status_code=400, detail="Could not decode image")

    height, width = image.shape[:2]

    faces = get_face_app().get(image)

    results: list[DetectedFace] = []
    for face in faces:
        x1, y1, x2, y2 = face.bbox
        x1, y1 = max(0.0, float(x1)), max(0.0, float(y1))
        x2, y2 = min(float(width), float(x2)), min(float(height), float(y2))

        results.append(
            DetectedFace(
                box_x=x1 / width,
                box_y=y1 / height,
                box_width=(x2 - x1) / width,
                box_height=(y2 - y1) / height,
                confidence=float(face.det_score),
                embedding=face.normed_embedding.tolist(),
            )
        )

    return DetectResponse(faces=results)
