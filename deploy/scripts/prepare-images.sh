#!/usr/bin/env bash
# Download/build all release dependencies before stopping writers. No service or storage mutations.
# The caller has already sourced deploy/.env and applied a validated IMAGE_TAG override.
set -euo pipefail
set +x

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "${SCRIPT_DIR}/.."
COMPOSE=(docker compose)
if [[ "${USE_HTTPS:-1}" == "1" ]]; then
  COMPOSE+=(--profile https)
fi

if [[ -n "${DOCKER_HUB_USERNAME:-}" && -n "${DOCKER_HUB_PASSWORD:-}" ]]; then
  echo "==> Logging in and preparing application images..."
  printf '%s' "${DOCKER_HUB_PASSWORD}" | docker login -u "${DOCKER_HUB_USERNAME}" --password-stdin
  "${COMPOSE[@]}" pull account shops moderation media gateway
else
  echo "==> Building application images (no registry credentials)..."
  "${COMPOSE[@]}" build account shops moderation media gateway
fi

echo "==> Preparing migration SDK, photo provisioning image and pinned renderer..."
docker pull mcr.microsoft.com/dotnet/sdk:10.0
"${COMPOSE[@]}" build minio-init
"${COMPOSE[@]}" pull checkin-renderer
