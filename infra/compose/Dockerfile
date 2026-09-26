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
WORKDIR /source
COPY . .
RUN dotnet publish "$PROJECT_PATH" --configuration Release --output /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update && apt-get install --yes --no-install-recommends git \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=codex-cli /usr/local /usr/local
COPY --from=github-mcp-server /out/github-mcp-server /usr/local/bin/github-mcp-server
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet"]
CMD ["Jarvis.Api.dll"]
