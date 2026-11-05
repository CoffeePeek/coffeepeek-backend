#!/usr/bin/env bash
# Publish all existing shelves once after migrations and before starting shelf writers.
set -euo pipefail

usage() {
  echo "Usage: bash dev/publish-all-shelves.sh [--dev]"
}

if [[ $# -gt 1 ]]; then
  usage >&2
  exit 2
fi

TASK_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
case "${1:-}" in
  --dev)
    SHELF_PSQL=(docker compose -f "${TASK_ROOT}/dev/docker-compose.dev.yml" exec -T postgres psql -X -v ON_ERROR_STOP=1 -U coffeepeek)
    ;;
  "")
    set -a
    # shellcheck disable=SC1091
    source "${TASK_ROOT}/deploy/.env"
    set +a
    : "${POSTGRES_USER:?POSTGRES_USER is required}"
    SHELF_PSQL=(docker compose --project-directory "${TASK_ROOT}/deploy" exec -T postgres psql -X -v ON_ERROR_STOP=1 -U "${POSTGRES_USER}")
    ;;
  --help|-h)
    usage
    exit 0
    ;;
  *)
    usage >&2
    exit 2
    ;;
esac

"${SHELF_PSQL[@]}" -d cpshopsdb < "${TASK_ROOT}/dev/publish-all-shelves.sql"
