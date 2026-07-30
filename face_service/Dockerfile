# Build from the REPO ROOT: docker build -f face_service/Dockerfile -t algoforge-face .
FROM python:3.12-slim
WORKDIR /app

# opencv-python (not the headless variant -- see requirements.txt) needs libGL at
# runtime, which a slim Debian image doesn't ship. This is exactly the same
# opencv-python-headless-vs-opencv-python conflict that bit the local dev setup, just
# showing up as a missing system library instead of a broken cv2 install.
RUN apt-get update && apt-get install -y --no-install-recommends \
        libgl1 \
        libglib2.0-0 \
    && rm -rf /var/lib/apt/lists/*

COPY face_service/requirements.txt .
# torch/torchvision's +cpu wheels only exist on PyTorch's own index; --extra-index-url
# adds it alongside PyPI rather than replacing it, since everything else here installs
# from PyPI as normal (see requirements.txt's own note on this).
RUN pip install --no-cache-dir --extra-index-url https://download.pytorch.org/whl/cpu \
        -r requirements.txt

COPY face_service/ .

# Pre-warm: triggers the exact same lazy model loading main.py's own startup event does,
# so the InsightFace/YOLO/OSNet weights (several hundred MB combined) are downloaded and
# cached into this image layer at build time instead of on every container start. Without
# this, the first request after every cold start pays that download and load cost --
# the thing PhotoDetectionWorker's warm_up comment already flags as a ~10s penalty
# locally, worse here since a fresh container has nothing cached at all.
RUN python -c "from pipeline import detectors, embeddings; detectors.get_face_app(); detectors.get_yolo(); embeddings.available_signals()"

# Render injects $PORT and expects the app to listen there; default covers hosts (like
# Azure Container Apps) that instead expect a fixed port you configure the platform with.
ENV PORT=8000
EXPOSE 8000
CMD ["sh", "-c", "uvicorn main:app --host 0.0.0.0 --port ${PORT}"]
