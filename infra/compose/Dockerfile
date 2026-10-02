# syntax=docker/dockerfile:1
ARG PROJECT_PATH

FROM node:22-slim AS codex-cli
RUN npm install --global @openai/codex@0.145.0

FROM golang:1.27.1-alpine AS github-mcp-server
ARG GITHUB_MCP_SERVER_VERSION=v1.12.2
RUN apk add --no-cache git
WORKDIR /source
RUN git clone --depth 1 --branch "$GITHUB_MCP_SERVER_VERSION" \
    https://github.com/github/github-mcp-server.git . && \
    mkdir -p /out && \
    CGO_ENABLED=0 go build -trimpath -ldflags="-s -w" \
      -o /out/github-mcp-server ./cmd/github-mcp-server

FROM mcr.microsoft.com/dotnet/sdk:10.0.302 AS publish
ARG PROJECT_PATH
ARG SENTRY_ORG=
ARG SENTRY_PROJECT=
ARG SENTRY_RELEASE=
ARG REQUIRE_WEB_CLIENT=false
WORKDIR /source
COPY . .
# The auth token is a BuildKit secret. Without it the image still builds and Sentry CLI stays off.
# With it, this publish uploads PDBs and source files for SENTRY_RELEASE (the git SHA).
RUN --mount=type=secret,id=sentry_auth_token,required=false \
    if [ -s /run/secrets/sentry_auth_token ]; then \
      export SENTRY_AUTH_TOKEN="$(cat /run/secrets/sentry_auth_token)"; \
      export SENTRY_RELEASE="${SENTRY_RELEASE}"; \
      dotnet publish "$PROJECT_PATH" --configuration Release --output /app/publish /p:UseAppHost=false \
        /p:SentryOrg="${SENTRY_ORG}" \
        /p:SentryProject="${SENTRY_PROJECT}" \
        /p:SentryCreateRelease=true \
        /p:SentryUploadSymbols=true \
        /p:SentryUploadSources=true \
        /p:DebugType=portable \
        /p:DebugSymbols=true \
        /p:ContinuousIntegrationBuild=true; \
    else \
      dotnet publish "$PROJECT_PATH" --configuration Release --output /app/publish /p:UseAppHost=false \
        /p:UseSentryCLI=false; \
    fi

# CI stages the web artifact outside Flutter's ignored build directory. Only
# the API serves it; production builds fail closed if the artifact is missing.
RUN set -eu; if [ "$PROJECT_PATH" = "src/Jarvis.Api/Jarvis.Api.csproj" ]; then \
      if [ "$REQUIRE_WEB_CLIENT" = "true" ]; then \
        for file in index.html main.dart.js flutter_bootstrap.js; do \
          test -s "/source/infra/web/dist/$file" || { echo "Missing web artifact: $file" >&2; exit 1; }; \
        done; \
      fi; \
      if [ -d /source/infra/web/dist ]; then \
        mkdir -p /app/publish/wwwroot && \
        cp -R /source/infra/web/dist/. /app/publish/wwwroot/; \
      fi; \
    fi

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update && apt-get install --yes --no-install-recommends git \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=codex-cli /usr/local /usr/local
COPY --from=github-mcp-server /out/github-mcp-server /usr/local/bin/github-mcp-server
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet"]
CMD ["Jarvis.Api.dll"]
