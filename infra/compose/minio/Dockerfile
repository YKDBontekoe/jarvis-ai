FROM golang:1.25-alpine AS build

ARG MINIO_VERSION=RELEASE.2025-10-15T17-29-55Z
ARG MC_VERSION=RELEASE.2025-08-13T08-35-41Z

RUN go install -trimpath -ldflags="-s -w" "github.com/minio/minio@${MINIO_VERSION}" \
    && go install -trimpath -ldflags="-s -w" "github.com/minio/mc@${MC_VERSION}"

FROM alpine:3.22

RUN apk add --no-cache ca-certificates tzdata
COPY --from=build /go/bin/minio /usr/local/bin/minio
COPY --from=build /go/bin/mc /usr/local/bin/mc

ENTRYPOINT ["minio"]
CMD ["server", "/data", "--console-address", ":9001"]
