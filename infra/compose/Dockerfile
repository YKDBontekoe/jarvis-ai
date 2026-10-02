# syntax=docker/dockerfile:1
ARG PROJECT_PATH

FROM node:22-slim@sha256:43ac6c60b8f89723f746e8a92ce91abd5017e627ce1ddfe4238355d3a30b772c AS codex-cli
WORKDIR /tmp/npm-patches
# npm 12.2.0 still bundles vulnerable patch versions; retain npm for CLI/MCP installs.
RUN npm install --global npm@12.2.0 && \
    npm pack brace-expansion@5.0.11 undici@6.28.1 && \
    rm -rf /usr/local/lib/node_modules/npm/node_modules/brace-expansion \
      /usr/local/lib/node_modules/npm/node_modules/undici && \
    mkdir -p /usr/local/lib/node_modules/npm/node_modules/brace-expansion \
      /usr/local/lib/node_modules/npm/node_modules/undici && \
    tar -xzf brace-expansion-5.0.11.tgz --strip-components=1 -C /usr/local/lib/node_modules/npm/node_modules/brace-expansion && \
    tar -xzf undici-6.28.1.tgz --strip-components=1 -C /usr/local/lib/node_modules/npm/node_modules/undici && \
    npm install --global @openai/codex@0.145.0 && \
    rm -f brace-expansion-5.0.11.tgz undici-6.28.1.tgz
WORKDIR /

FROM golang:1.27.1-alpine@sha256:8a5910f31396cd4d89662f56c68b3ae31d374308270a1c3bd96672ee5ed43414 AS github-mcp-server
ARG GITHUB_MCP_SERVER_VERSION=v1.13.0
ARG GITHUB_MCP_SERVER_REVISION=85b0399bf214f24f5028d9cedecdd020f646c822
RUN apk add --no-cache git
WORKDIR /source
RUN git init . && \
    git remote add origin https://github.com/github/github-mcp-server.git && \
    git fetch --depth 1 origin "refs/tags/${GITHUB_MCP_SERVER_VERSION}:refs/tags/${GITHUB_MCP_SERVER_VERSION}" && \
    test "$(git rev-parse "${GITHUB_MCP_SERVER_VERSION}^{commit}")" = "$GITHUB_MCP_SERVER_REVISION" && \
    git checkout --detach "$GITHUB_MCP_SERVER_REVISION" && \
    go get golang.org/x/net@v0.56.0 golang.org/x/text@v0.39.0 && \
    mkdir -p /out && \
    CGO_ENABLED=0 go build -trimpath -ldflags="-s -w" \
      -o /out/github-mcp-server ./cmd/github-mcp-server

FROM mcr.microsoft.com/dotnet/sdk:10.0.302@sha256:72dd743782f2ae7e5476fd64f6a460045e3998dc862218b80e6944cba79a01b0 AS publish
ARG PROJECT_PATH
ARG SENTRY_ORG=
ARG SENTRY_PROJECT=
ARG SENTRY_RELEASE=
WORKDIR /source
COPY . .
# The auth token is a BuildKit secret. Without it the image still builds and Sentry CLI stays off.
# With it, this publish uploads PDBs and source files for SENTRY_RELEASE (the git SHA).
RUN --mount=type=secret,id=sentry_auth_token,required=false \
    if [ -s /run/secrets/sentry_auth_token ]; then \
      SENTRY_AUTH_TOKEN="$(cat /run/secrets/sentry_auth_token)"; \
      export SENTRY_AUTH_TOKEN; \
      export SENTRY_RELEASE="${SENTRY_RELEASE}"; \
      dotnet publish "$PROJECT_PATH" --configuration Release --output /app/publish /p:UseAppHost=false /p:EmbedAllSources=true \
        /p:SentryOrg="${SENTRY_ORG}" \
        /p:SentryProject="${SENTRY_PROJECT}" \
        /p:SentryCreateRelease=true \
        /p:SentryUploadSymbols=true \
        /p:SentryUploadSources=true \
        /p:DebugType=portable \
        /p:DebugSymbols=true \
        /p:ContinuousIntegrationBuild=true; \
    else \
      dotnet publish "$PROJECT_PATH" --configuration Release --output /app/publish /p:UseAppHost=false /p:EmbedAllSources=true \
        /p:UseSentryCLI=false; \
    fi

FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:2d584d8147faddb0d678c5748d47953e5b8e18621ed4fb7049a91381d9d7746f AS runtime
RUN apt-get update && apt-get install --yes --no-install-recommends git libssl3t64 openssl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=codex-cli /usr/local /usr/local
COPY --from=github-mcp-server /out/github-mcp-server /usr/local/bin/github-mcp-server
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet"]
CMD ["Jarvis.Api.dll"]
