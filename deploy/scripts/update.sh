#!/usr/bin/env bash
# Pull latest app images and restart services (used by CI and manual updates).
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DEPLOY_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"
cd "${DEPLOY_DIR}"

bash "${SCRIPT_DIR}/ensure-secrets.sh"

set -a
# shellcheck disable=SC1091
source .env
set +a

bash "${SCRIPT_DIR}/check-coffee-import.sh"

COMPOSE=(docker compose)
if [[ "${USE_HTTPS:-1}" == "1" ]]; then
  COMPOSE+=(--profile https)
fi

if [[ -n "${DOCKER_HUB_USERNAME:-}" && -n "${DOCKER_HUB_PASSWORD:-}" ]]; then
  echo "==> Logging in to Docker Hub..."
  echo "${DOCKER_HUB_PASSWORD}" | docker login -u "${DOCKER_HUB_USERNAME}" --password-stdin
fi

wait_for_backend() {
  local service="$1"
  local path="${2:-/health}"
  local attempts="${3:-40}"

  echo "==> Waiting for ${service} (${path})..."
  for ((i = 1; i <= attempts; i++)); do
    if "${COMPOSE[@]}" exec -T "$service" curl --connect-timeout 2 --max-time 3 \
      -sf "http://${service}:8080${path}" >/dev/null 2>&1; then
      echo "${service} is ready"
      return 0
    fi
    sleep 3
  done

  echo "${service} did not become ready in time"
  echo "==> Recent ${service} logs..."
  "${COMPOSE[@]}" logs --no-color --tail 100 "$service" || true
  "${COMPOSE[@]}" ps -a "$service"
  local container_id
  container_id="$("${COMPOSE[@]}" ps -a -q "$service")"
  if [[ -n "$container_id" ]]; then
    docker inspect --format 'Status={{.State.Status}} ExitCode={{.State.ExitCode}} OOMKilled={{.State.OOMKilled}} RestartCount={{.RestartCount}} Error={{.State.Error}}' "$container_id"
  fi
  return 1
}

use_registry_images=false
if [[ -n "${DOCKER_HUB_USERNAME:-}" && -n "${DOCKER_HUB_PASSWORD:-}" ]]; then
  use_registry_images=true
fi

# Prepare every image before replacing any running backend.
if [[ "${use_registry_images}" == "true" ]]; then
  echo "==> Pulling application images from Docker Hub..."
  "${COMPOSE[@]}" pull account shops moderation media gateway
else
  echo "==> Building application images (no registry credentials)..."
  "${COMPOSE[@]}" build account shops moderation media gateway
fi

# Run the current mounted provisioning script even when the one-shot container already completed.
echo "==> Provisioning private photo storage and the read-only reader..."
"${COMPOSE[@]}" up -d minio
"${COMPOSE[@]}" run --rm --no-deps minio-init

# Start one backend at a time to avoid simultaneous initialization spikes.
for service in account media shops moderation; do
  if [[ -n "${COFFEE_IMPORT_TOKEN:-}" && ( "${service}" == "media" || "${service}" == "shops" ) ]]; then
    # A single-file bind mount can retain the old inode after an atomic mapping update.
    # Recreate the import hosts even when their image tag and mount path did not change.
    "${COMPOSE[@]}" up -d --no-deps --no-build --force-recreate "$service"
  else
    "${COMPOSE[@]}" up -d --no-build "$service"
  fi
  wait_for_backend "$service" /health
done
wait_for_backend shops /api/Catalogs/cities

echo "==> Starting gateway..."
# Backends were already updated and checked; do not recreate dependencies here.
"${COMPOSE[@]}" up -d --no-deps --no-build gateway
wait_for_backend gateway /health
"${COMPOSE[@]}" up -d --no-deps imgproxy
if [[ "${USE_HTTPS:-1}" == "1" ]]; then
  # Caddyfile is a single-file bind mount: rsync swaps in a new inode, so a running container keeps
  # the old file (and `caddy reload` would re-read it). Recreate only when the contents differ.
  if "${COMPOSE[@]}" exec -T caddy cat /etc/caddy/Caddyfile 2>/dev/null | cmp -s - Caddyfile; then
    "${COMPOSE[@]}" up -d --no-deps caddy
  else
    echo "==> Caddyfile changed, recreating caddy..."
    "${COMPOSE[@]}" up -d --no-deps --force-recreate caddy
  fi
fi

echo "==> Health check..."
curl --connect-timeout 2 --max-time 10 -sf "http://localhost:${GATEWAY_PORT:-8080}/api/Catalogs/cities" >/dev/null
echo "OK — gateway can reach shops"

"${COMPOSE[@]}" ps
