#!/usr/bin/env bash
# Build/pull images and start the CoffeePeek stack.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DEPLOY_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"
REPO_ROOT="$(cd "${DEPLOY_DIR}/.." && pwd)"
cd "${DEPLOY_DIR}"

if [[ ! -f .env ]]; then
  cp .env.example .env
  echo "Created .env from .env.example — edit secrets before production use."
fi

bash "${SCRIPT_DIR}/ensure-secrets.sh"

set -a
# shellcheck disable=SC1091
source .env
set +a

bash "${SCRIPT_DIR}/check-coffee-import.sh"

if [[ "${JWT_SECRET_KEY:-}" == change-me* ]] || [[ "${POSTGRES_PASSWORD:-}" == change-me* ]]; then
  echo "Warning: default secrets detected in .env — change them before production."
fi

COMPOSE=(docker compose)
if [[ "${USE_HTTPS:-1}" == "1" ]]; then
  COMPOSE+=(--profile https)
fi

echo "==> Starting infrastructure (postgres, redis, rabbitmq, minio)..."
"${COMPOSE[@]}" up -d postgres redis rabbitmq minio minio-init

echo "==> Waiting for PostgreSQL..."
until "${COMPOSE[@]}" exec -T postgres pg_isready -U "${POSTGRES_USER}" >/dev/null 2>&1; do
  sleep 2
done

if [[ "${SKIP_MIGRATIONS:-}" != "1" ]]; then
  running_apps="$("${COMPOSE[@]}" ps -q gateway account shops moderation media)"
  if [[ -n "${running_apps}" ]]; then
    echo "==> Stopping application writers before schema updates..."
    "${COMPOSE[@]}" stop gateway account shops moderation media
  fi
  echo "==> Applying EF Core migrations via Docker..."
  docker run --rm \
    --network coffeepeek_default \
    -v "${REPO_ROOT}:/src" \
    -w /src \
    -e POSTGRES_USER \
    -e POSTGRES_PASSWORD \
    -e PG_HOST=postgres \
    -e PG_PORT=5432 \
    -e ASPNETCORE_ENVIRONMENT=Production \
    -e DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    mcr.microsoft.com/dotnet/sdk:10.0 \
    bash deploy/scripts/apply-migrations.sh
  bash "${REPO_ROOT}/dev/backfill-profile-roaster-counts.sh"
fi

echo "==> Building and starting application services..."
if [[ "${PULL_IMAGES:-}" == "1" ]]; then
  "${COMPOSE[@]}" pull account shops moderation media gateway
  "${COMPOSE[@]}" up -d --no-build
else
  "${COMPOSE[@]}" up -d --build
fi
if [[ -n "${COFFEE_IMPORT_TOKEN:-}" ]]; then
  # Refresh the mapping bind mount and cached options after configuration updates.
  "${COMPOSE[@]}" up -d --no-deps --no-build --force-recreate --wait media shops
fi

echo "==> Stack status:"
"${COMPOSE[@]}" ps

cat <<EOF

Deploy finished.

API (HTTPS):    https://${DOMAIN:-api.example.com}
Health (local): http://127.0.0.1:${GATEWAY_PORT:-8080}/health
Scalar (local): http://127.0.0.1:${GATEWAY_PORT:-8080}/scalar

Gateway is bound to loopback only. Public traffic goes through Caddy on :443.

Logs: docker compose -f ${DEPLOY_DIR}/docker-compose.yml logs -f gateway

EOF
