# Official release binary, pinned and verified independently of the retired mc registry image.
FROM alpine:3.22 AS download
RUN apk add --no-cache ca-certificates curl
ARG TARGETARCH
RUN set -eu; \
    case "${TARGETARCH:-$(apk --print-arch)}" in \
      amd64|x86_64) arch=amd64; expected=01f866e9c5f9b87c2b09116fa5d7c06695b106242d829a8bb32990c00312e891 ;; \
      arm64|aarch64) arch=arm64; expected=14c8c9616cfce4636add161304353244e8de383b2e2752c0e9dad01d4c27c12c ;; \
      *) echo "Unsupported mc architecture" >&2; exit 1 ;; \
    esac; \
    curl --fail --location --proto '=https' --tlsv1.2 \
      "https://github.com/minio/mc/releases/download/RELEASE.2025-08-13T08-35-41Z/mc.linux-${arch}.RELEASE.2025-08-13T08-35-41Z" \
      --output /tmp/mc; \
    echo "$expected  /tmp/mc" | sha256sum -c -; \
    chmod 755 /tmp/mc

FROM alpine:3.22
RUN apk add --no-cache ca-certificates
COPY --from=download /tmp/mc /usr/bin/mc
ENTRYPOINT ["/usr/bin/mc"]
