#!/usr/bin/env bash
# Run all CoffeePeek services locally in Development against the dev infra.
#
# Prereqs:
#   1. Infra running:   docker compose -f dev/docker-compose.dev.yml up -d
#   2. DBs migrated:    dev/migrate-and-seed.sh   (migrations + role seed)
#
# Usage:
#   dev/run-services.sh            # build once, then start all 5 services
#   dev/run-services.sh --no-build # skip build (services already built)
#
# Logs go to dev/logs/<service>.log ; PIDs to dev/logs/<service>.pid
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

export DOTNET_ROOT=${DOTNET_ROOT:-/usr/share/dotnet}
export PATH="$PATH:$DOTNET_ROOT:$HOME/.dotnet/tools"

# shellcheck disable=SC1091
set -a; source dev/dev.env; set +a

LOG_DIR="$ROOT/dev/logs"
mkdir -p "$LOG_DIR"

BUILD=1
[[ "${1:-}" == "--no-build" ]] && BUILD=0

pg() { echo "Host=localhost;Port=5432;Database=$1;Username=coffeepeek;Password=coffeepeek;SslMode=Disable"; }

if [[ "$BUILD" == "1" ]]; then
  echo "Building services (Debug)..."
  dotnet build CoffeePeek.Backend.ci.slnf -c Debug -m -p:AllowMissingPrunePackageData=true >/dev/null
fi

# Run the built DLLs directly (not `dotnet run`) so the tracked PID is the actual
# app process and can be stopped reliably (dotnet run does not forward signals).
start() {
  local name="$1" proj="$2"; shift 2
  local dll="$ROOT/$proj/bin/Debug/net10.0/$proj.dll"
  if [[ ! -f "$dll" ]]; then echo "Missing $dll — run without --no-build first." >&2; exit 1; fi
  echo "Starting $name ..."
  ( cd "$ROOT/$proj" && env "$@" nohup dotnet "$dll" \
      >"$LOG_DIR/$name.log" 2>&1 & echo $! >"$LOG_DIR/$name.pid" )
}

start account CoffeePeek.AccountService \
  ASPNETCORE_URLS=http://localhost:5353 \
  PostgresCpOptions__ConnectionString="$(pg cpaccountdb)" \
  MinIOOptions__BucketName=coffeepeek.avatars \
  AdminStatsOptions__ShopsServiceUrl=http://localhost:5243 \
  AdminStatsOptions__ModerationServiceUrl=http://localhost:6453

start shops CoffeePeek.ShopsService \
  ASPNETCORE_URLS=http://localhost:5243 \
  PostgresCpOptions__ConnectionString="$(pg cpshopsdb)" \
  services__coffeepeek-accountservice__http__0=http://localhost:5353 \
  services__coffeepeek-mediaservice__http__0=http://localhost:5025

start moderation CoffeePeek.ModerationService \
  ASPNETCORE_URLS=http://localhost:6453 \
  PostgresCpOptions__ConnectionString="$(pg cpmoderationdb)" \
  MinIOOptions__BucketName=coffeepeek.shops \
  services__coffeepeek-shopsservice__http__0=http://localhost:5243 \
  services__coffeepeek-mediaservice__http__0=http://localhost:5025

start media CoffeePeek.MediaService \
  ASPNETCORE_URLS=http://localhost:5025 \
  PostgresCpOptions__ConnectionString="$(pg cpmediadb)" \
  MinIOOptions__ShopBucketName=coffeepeek.shops \
  MinIOOptions__UserBucketName=coffeepeek.avatars

start gateway CoffeePeek.Gateway \
  ASPNETCORE_URLS=http://localhost:8080

echo
echo "All services launched. Tail logs with: tail -f dev/logs/<service>.log"
echo "Gateway:     http://localhost:8080  (Scalar docs at /scalar)"
echo "Account:5353 Shops:5243 Moderation:6453 Media:5025"
