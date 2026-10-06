#!/usr/bin/env bash
# Run through the systemd service so manual and scheduled jobs cannot overlap.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
IMPORT_PYTHON=/opt/coffeepeek-coffee-import/.venv/bin/python

"${IMPORT_PYTHON}" "${REPO_ROOT}/dev/coffee-import/validate_deployment.py" --job
IFS=',' read -r -a sources <<< "${COFFEE_IMPORT_SOURCES}"
args=()
for source in "${sources[@]}"; do
  args+=(--source "${source}")
done
exec "${IMPORT_PYTHON}" -X utf8 "${REPO_ROOT}/dev/coffee-import/import_coffees.py" \
  --output /var/lib/coffeepeek-coffee-import --push "${args[@]}"
