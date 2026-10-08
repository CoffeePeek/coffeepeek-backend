#!/usr/bin/env bash
# Apply EF Core migrations to the VPS PostgreSQL instance.
# Requires .NET 10 SDK on the host (install: https://dot.net).
set -euo pipefail

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

cd "${REPO_ROOT}"
dotnet restore CoffeePeek.Backend.ci.slnf -p:AllowMissingPrunePackageData=true
dotnet build CoffeePeek.Backend.ci.slnf --configuration Release --no-restore -m -p:AllowMissingPrunePackageData=true

# Application writers must be stopped. This runner first upgrades to the canonical
# schema, transfers recoverable legacy data and private photos, then removes archives.
# It creates no backups and is safe to rerun after an interrupted deployment.
dotnet "${REPO_ROOT}/CoffeePeek.LegacyCheckInMigration/bin/Release/net10.0/CoffeePeek.LegacyCheckInMigration.dll"

echo "All migrations applied."
