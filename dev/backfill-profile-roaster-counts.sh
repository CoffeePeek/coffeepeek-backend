#!/usr/bin/env bash
# Reconcile added-roaster statistics across Moderation and Account after EF migrations.
# Run while application writers are stopped; safe to repeat before every deployment.
set -euo pipefail

TASK_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
if [[ "${1:-}" == "--dev" ]]; then
  PROFILE_PG_USER=coffeepeek
  PROFILE_PSQL=(docker compose -f "${TASK_ROOT}/dev/docker-compose.dev.yml" exec -T postgres psql -X -q -v ON_ERROR_STOP=1 -U "${PROFILE_PG_USER}")
elif [[ $# == 0 ]]; then
  set -a
  # shellcheck disable=SC1091
  source "${TASK_ROOT}/deploy/.env"
  set +a
  : "${POSTGRES_USER:?POSTGRES_USER is required}"
  PROFILE_PSQL=(docker compose --project-directory "${TASK_ROOT}/deploy" exec -T postgres psql -X -q -v ON_ERROR_STOP=1 -U "${POSTGRES_USER}")
else
  echo "Usage: bash dev/backfill-profile-roaster-counts.sh [--dev]" >&2
  exit 2
fi

PROFILE_CSV="$(mktemp)"
trap 'rm -f -- "${PROFILE_CSV}"' EXIT
# Export must finish successfully before Account is changed. No CSV or credentials remain in the checkout.
"${PROFILE_PSQL[@]}" -d cpmoderationdb -c \
  'COPY (SELECT "Id", "UserId" FROM "ModerationRoasters" WHERE "ModerationStatus" = 1 AND "UserId" IS NOT NULL) TO STDOUT WITH (FORMAT csv)' > "${PROFILE_CSV}"

{
  cat <<'SQL'
BEGIN;
CREATE TEMP TABLE profile_roaster_backfill ("RoasterId" uuid PRIMARY KEY, "UserId" uuid NOT NULL) ON COMMIT DROP;
COPY profile_roaster_backfill ("RoasterId", "UserId") FROM STDIN WITH (FORMAT csv);
SQL
  cat "${PROFILE_CSV}"
  printf '\\.\n'
  cat <<'SQL'
INSERT INTO "UserRoasterCredits" ("RoasterId", "UserId", "CreatedAtUtc")
SELECT r."RoasterId", r."UserId", now()
FROM profile_roaster_backfill r
JOIN "Users" u ON u."Id" = r."UserId"
WHERE NOT u."IsSoftDelete"
ON CONFLICT ("RoasterId") DO NOTHING;

UPDATE "Users" u
SET "AddedRoastersCount" = c.total, "StatisticUpdatedAtUtc" = now()
FROM (
    SELECT u2."Id", count(r."RoasterId")::integer AS total
    FROM "Users" u2 LEFT JOIN "UserRoasterCredits" r ON r."UserId" = u2."Id"
    GROUP BY u2."Id"
) c
WHERE u."Id" = c."Id" AND u."AddedRoastersCount" IS DISTINCT FROM c.total;
COMMIT;
SQL
} | "${PROFILE_PSQL[@]}" -d cpaccountdb

echo "Profile roaster statistics reconciled."
