#!/usr/bin/env bash
# Apply EF Core migrations to the 4 CoffeePeek databases and seed the Roles table.
# Requires the dev infra to be running (dev/docker-compose.dev.yml).
# The repository tool manifest provides dotnet-ef.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

export DOTNET_ROOT=${DOTNET_ROOT:-/usr/share/dotnet}
export PATH="$PATH:$DOTNET_ROOT:$HOME/.dotnet/tools"

# shellcheck disable=SC1091
set -a; source dev/dev.env; set +a

PG_CONTAINER=${PG_CONTAINER:-coffeepeek-dev-infra-postgres-1}

dotnet tool restore

apply_migrations() {
  local connection="$1" project="$2" startup="$3" context="$4"
  export PostgresCpOptions__ConnectionString="${connection}"
  dotnet ef database update \
    --project "${project}" \
    --startup-project "${startup}" \
    --context "${context}" \
    --configuration Debug
}

echo "== apply database migrations =="
apply_migrations "${ConnectionStrings__cpaccountdb}" CoffeePeek.Account.Persistence CoffeePeek.AccountService CoffeePeek.Account.Persistence.Configuration.AccountDbContext
apply_migrations "${ConnectionStrings__cpshopsdb}" CoffeePeek.Shops.Persistance CoffeePeek.ShopsService CoffeePeek.Shops.Persistance.Configuration.ShopsDbContext
apply_migrations "${ConnectionStrings__cpmoderationdb}" CoffeeShop.Moderation.Persistence CoffeePeek.ModerationService CoffeeShop.Moderation.Persistence.Configuration.ModerationDbContext
apply_migrations "${ConnectionStrings__cpmediadb}" CoffeePeek.MediaService CoffeePeek.MediaService CoffeePeek.MediaService.Data.MediaDbContext

echo "== seed roles (cpaccountdb) =="
docker exec "$PG_CONTAINER" psql -U coffeepeek -d cpaccountdb -c \
  "INSERT INTO \"Roles\" (\"Id\", \"Name\", \"CreatedAtUtc\") \
   SELECT gen_random_uuid(), r, now() FROM (VALUES ('User'),('Admin'),('Moderator'),('Owner'),('Employee'),('Roaster')) AS t(r) \
   WHERE NOT EXISTS (SELECT 1 FROM \"Roles\" WHERE \"Name\" = t.r);"

echo "== backfill added-roaster statistics (application writers must be stopped) =="
bash dev/backfill-profile-roaster-counts.sh --dev

echo "Done."
