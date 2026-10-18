#!/usr/bin/env bash
# Restore tools/packages and build migration startup projects before stopping writers.
# Run in the same SDK image, checkout and /root cache volumes as apply-migrations.sh --prepared.
# Preparation does not load production configuration or create a DbContext.
set -euo pipefail
set +x

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
cd "${REPO_ROOT}"

dotnet tool restore
for startup in CoffeePeek.AccountService CoffeePeek.ShopsService CoffeePeek.ModerationService CoffeePeek.MediaService; do
  echo "==> Preparing ${startup} migrations..."
  dotnet restore "${startup}/${startup}.csproj" --disable-parallel
  dotnet build "${startup}/${startup}.csproj" --configuration Release --no-restore -m:1
done

echo "Migration tools and Release assemblies prepared."
