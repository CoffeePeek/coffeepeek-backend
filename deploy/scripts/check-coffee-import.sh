#!/usr/bin/env bash
# Called with deploy/.env exported. No credentials are printed.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
mapping="${COFFEE_IMPORT_MAPPING_FILE:-./coffee-import/disabled-mapping.json}"
if [[ "${mapping}" != /* ]]; then
  mapping="${REPO_ROOT}/deploy/${mapping}"
fi
python3 "${REPO_ROOT}/dev/coffee-import/validate_deployment.py" --mapping "${mapping}" --allow-disabled

if [[ -n "${COFFEE_IMPORT_TOKEN:-}" ]]; then
  python3 "${REPO_ROOT}/dev/coffee-import/validate_deployment.py" --mapping "${mapping}" --job-env /etc/coffeepeek/coffee-import.env
  runtime=/opt/coffeepeek-coffee-import
  if [[ ! -x "${runtime}/.venv/bin/python" || ! -f "${runtime}/requirements.sha256" ]]; then
    echo "Coffee import runtime is missing. Run sudo deploy/scripts/setup-coffee-import.sh --mapping <reviewed.json>." >&2
    exit 1
  fi
  expected="$(sha256sum "${REPO_ROOT}/dev/coffee-import/requirements.txt" | cut -d ' ' -f1)"
  if [[ "$(cat "${runtime}/requirements.sha256")" != "${expected}" ]]; then
    echo "Coffee import requirements changed. Re-run sudo deploy/scripts/setup-coffee-import.sh --mapping <reviewed.json> before deploying." >&2
    exit 1
  fi
fi
