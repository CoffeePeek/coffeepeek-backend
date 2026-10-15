#!/usr/bin/env bash
# CI deploy entrypoint on VPS: migrations (docker) + pull/restart app containers.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DEPLOY_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"
REPO_ROOT="$(cd "${DEPLOY_DIR}/.." && pwd)"

cd "${DEPLOY_DIR}"
set -a
# shellcheck disable=SC1091
source .env
set +a

bash "${SCRIPT_DIR}/check-coffee-import.sh"

echo "==> Ensuring infrastructure is up..."
docker compose up -d postgres redis rabbitmq minio
until docker compose exec -T postgres pg_isready -U "${POSTGRES_USER}" >/dev/null 2>&1; do
  sleep 2
done

if [[ "${SKIP_MIGRATIONS:-}" != "1" ]]; then
  echo "==> Stopping application writers for schema migrations..."
  docker compose stop gateway account shops moderation media
  # On failure keep writers stopped; rerun ci-deploy.sh to finish and restart.
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

bash "${SCRIPT_DIR}/update.sh"
