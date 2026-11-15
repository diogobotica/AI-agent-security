# worker image: Python 3.12 slim, runtime dependencies only (requirements.txt), run as a non-root user.
# The base image is pinned by tag and digest.

FROM python:3.12.15-slim-trixie@sha256:a0f57064cbdcd6285fa45f51237ab0b35ea8ebaed06a1f5973e89e19dfb86704

ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    PIP_NO_CACHE_DIR=1 \
    PIP_DISABLE_PIP_VERSION_CHECK=1

WORKDIR /app

# Install dependencies first so that layer is cached while only source files change.
COPY requirements.txt ./
RUN pip install --no-cache-dir -r requirements.txt

COPY src/ src/

# Fixed non-root user with no home directory and no login shell.
RUN useradd --system --uid 10001 --user-group --no-create-home --shell /usr/sbin/nologin app
USER 10001:10001

# Inside the container uvicorn listens on all interfaces; the host publishes the port on 127.0.0.1 only.
EXPOSE 8001

# Liveness check with the standard library (no curl in the image).
HEALTHCHECK --interval=10s --timeout=5s --start-period=30s --retries=3 \
  CMD ["python", "-c", "import json, urllib.request; r = urllib.request.urlopen('http://127.0.0.1:8001/health', timeout=3); assert json.load(r) == {'status': 'ok'}"]

CMD ["uvicorn", "worker.main:app", "--app-dir", "src", "--host", "0.0.0.0", "--port", "8001"]
