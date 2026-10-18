#!/usr/bin/env bash
# Apply EF Core migrations to the four VPS databases.
# CI runs this inside the .NET 10 SDK container; the repository tool manifest
# provides dotnet-ef.
set -euo pipefail
set +x

prepared=false
if [[ "$#" == 1 && "$1" == --prepared ]]; then
  prepared=true
elif [[ "$#" != 0 ]]; then
  echo "Usage: apply-migrations.sh [--prepared]" >&2
  exit 1
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DEPLOY_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"
REPO_ROOT="$(cd "${DEPLOY_DIR}/.." && pwd)"

if [[ ! -f "${DEPLOY_DIR}/.env" ]]; then
  echo "Missing ${DEPLOY_DIR}/.env"
  exit 1
fi

set -a
# shellcheck disable=SC1091
source "${DEPLOY_DIR}/.env"
set +a

if ! command -v dotnet >/dev/null 2>&1; then
  echo "dotnet SDK not found."
  exit 1
fi

export ASPNETCORE_ENVIRONMENT=Production
export PG_HOST="${PG_HOST:-localhost}"
export PG_PORT="${PG_PORT:-5432}"
: "${POSTGRES_USER:?POSTGRES_USER is required}"
: "${POSTGRES_PASSWORD:?POSTGRES_PASSWORD is required}"
# Npgsql reads credentials from these variables when they are absent from the
# connection string. This preserves passwords containing semicolons or quotes.
export PGUSER="${POSTGRES_USER}"
export PGPASSWORD="${POSTGRES_PASSWORD}"

cd "${REPO_ROOT}"
if [[ "${prepared}" == false ]]; then
  dotnet tool restore
fi

apply_migrations() {
  local database="$1" project="$2" startup="$3" context="$4"
  local quoted_host="${PG_HOST//\"/\"\"}"
  export PostgresCpOptions__ConnectionString="Host=\"${quoted_host}\";Port=${PG_PORT};Database=${database};SslMode=Disable;GSS Encryption Mode=Disable"
  echo "==> Applying ${database} migrations..."
  local prepared_options=()
  if [[ "${prepared}" == true ]]; then
    # EF --no-build also skips its implicit restore; all tools and assemblies were prepared before downtime.
    prepared_options+=(--no-build)
  fi
  dotnet ef database update \
    --project "${project}" \
    --startup-project "${startup}" \
    --context "${context}" \
    --configuration Release \
    "${prepared_options[@]}"
}

apply_migrations cpaccountdb CoffeePeek.Account.Persistence CoffeePeek.AccountService CoffeePeek.Account.Persistence.Configuration.AccountDbContext
apply_migrations cpshopsdb CoffeePeek.Shops.Persistance CoffeePeek.ShopsService CoffeePeek.Shops.Persistance.Configuration.ShopsDbContext
apply_migrations cpmoderationdb CoffeeShop.Moderation.Persistence CoffeePeek.ModerationService CoffeeShop.Moderation.Persistence.Configuration.ModerationDbContext
apply_migrations cpmediadb CoffeePeek.MediaService CoffeePeek.MediaService CoffeePeek.MediaService.Data.MediaDbContext

echo "All migrations applied."
