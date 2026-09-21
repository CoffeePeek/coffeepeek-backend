#!/usr/bin/env bash
# Build/pull images and start the CoffeePeek stack.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DEPLOY_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"
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

if [[ "${JWT_SECRET_KEY:-}" == change-me* ]] || [[ "${POSTGRES_PASSWORD:-}" == change-me* ]]; then
  echo "Warning: default secrets detected in .env — change them before production."
fi

COMPOSE=(docker compose)
if [[ "${USE_HTTPS:-}" == "1" ]]; then
  COMPOSE+=(--profile https)
fi

echo "==> Starting infrastructure (postgres, redis, rabbitmq, minio)..."
"${COMPOSE[@]}" up -d postgres redis rabbitmq minio minio-init

echo "==> Waiting for PostgreSQL..."
until "${COMPOSE[@]}" exec -T postgres pg_isready -U "${POSTGRES_USER}" >/dev/null 2>&1; do
  sleep 2
done

if [[ "${SKIP_MIGRATIONS:-}" != "1" ]]; then
  echo "==> Applying EF Core migrations..."
  "${SCRIPT_DIR}/apply-migrations.sh"
fi

echo "==> Building and starting application services..."
if [[ "${PULL_IMAGES:-}" == "1" ]]; then
  "${COMPOSE[@]}" pull account shops moderation media gateway || true
fi

"${COMPOSE[@]}" up -d --build

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
