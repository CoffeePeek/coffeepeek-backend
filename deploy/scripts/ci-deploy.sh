#!/usr/bin/env bash
# CI deploy entrypoint on VPS: migrations (docker) + pull/restart app containers.
set -euo pipefail
set +x
requested_image_tag="${DEPLOY_IMAGE_TAG:-}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DEPLOY_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"
REPO_ROOT="$(cd "${DEPLOY_DIR}/.." && pwd)"

cd "${DEPLOY_DIR}"
bash "${SCRIPT_DIR}/ensure-secrets.sh"
set -a
# shellcheck disable=SC1091
source .env
set +a

if [[ -n "${requested_image_tag}" ]]; then
  python3 "${SCRIPT_DIR}/persist_image_tag.py" --check .env "${requested_image_tag}"
  export IMAGE_TAG="${requested_image_tag}"
fi

bash "${SCRIPT_DIR}/check-coffee-import.sh"

# Registry/build/network failures must occur while the old application is still running.
bash "${SCRIPT_DIR}/prepare-images.sh"
if [[ "${SKIP_MIGRATIONS:-}" != "1" ]]; then
  # Resolve once: preparation and applying use the exact same local SDK even if its registry tag moves.
  migration_sdk_image="$(docker image inspect --format '{{.Id}}' mcr.microsoft.com/dotnet/sdk:10.0)"
  if [[ ! "${migration_sdk_image}" =~ ^sha256:[0-9a-f]{64}$ ]]; then
    echo "Migration SDK image is unavailable." >&2
    exit 1
  fi
  echo "==> Preparing EF Core tools and assemblies before writer downtime..."
  docker run --rm --pull=never \
    -v "${REPO_ROOT}:/src" \
    -v coffeepeek_migration_nuget:/root/.nuget \
    -v coffeepeek_migration_dotnet:/root/.dotnet \
    -w /src \
    -e DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    "${migration_sdk_image}" \
    bash deploy/scripts/prepare-migrations.sh
fi
if [[ -n "${requested_image_tag}" ]]; then
  python3 "${SCRIPT_DIR}/persist_image_tag.py" .env "${requested_image_tag}"
fi

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
  docker run --rm --pull=never \
    --network coffeepeek_default \
    -v "${REPO_ROOT}:/src" \
    -v coffeepeek_migration_nuget:/root/.nuget \
    -v coffeepeek_migration_dotnet:/root/.dotnet \
    -w /src \
    -e POSTGRES_USER \
    -e POSTGRES_PASSWORD \
    -e PG_HOST=postgres \
    -e PG_PORT=5432 \
    -e ASPNETCORE_ENVIRONMENT=Production \
    -e DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    "${migration_sdk_image}" \
    bash deploy/scripts/apply-migrations.sh --prepared
  bash "${REPO_ROOT}/dev/backfill-profile-roaster-counts.sh"
fi

# Images were prepared before the writer stop; do not require a registry round-trip during downtime.
bash "${SCRIPT_DIR}/update.sh" --images-prepared
