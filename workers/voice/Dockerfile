FROM node:22-bookworm-slim AS codex-cli
RUN npm install --global @openai/codex@0.145.0

FROM python:3.12-slim
ENV PYTHONUNBUFFERED=1 \
    PIP_DISABLE_PIP_VERSION_CHECK=1
WORKDIR /app
COPY --from=codex-cli /usr/local /usr/local
COPY workers/voice/pyproject.toml workers/voice/uv.lock workers/voice/.python-version ./
RUN pip install --no-cache-dir uv==0.11.22 \
    && uv sync --locked --no-dev --no-install-project
COPY workers/voice/voice_worker.py workers/voice/playback_gate.py ./
ENTRYPOINT ["/app/.venv/bin/python", "/app/voice_worker.py"]
CMD ["start"]
