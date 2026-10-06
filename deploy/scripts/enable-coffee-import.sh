#!/usr/bin/env bash
# Explicit final step; setup and deployment never enable the daily timer.
set -euo pipefail
if [[ "${EUID}" != "0" ]]; then
  echo "Run this command with sudo after the single-source smoke test." >&2
  exit 1
fi
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
set -a
# shellcheck disable=SC1091
source "${REPO_ROOT}/deploy/.env"
set +a
bash "${REPO_ROOT}/deploy/scripts/check-coffee-import.sh"
python3 "${REPO_ROOT}/dev/coffee-import/validate_deployment.py" --mapping "${COFFEE_IMPORT_MAPPING_FILE}" --job-env /etc/coffeepeek/coffee-import.env
python3 - "${REPO_ROOT}" <<'PY'
import json
import sys
from pathlib import Path
sys.path.insert(0, sys.argv[1] + "/dev/coffee-import")
from verify_deployment_smoke import fingerprint
marker = Path("/etc/coffeepeek/coffee-import-smoke.json")
assert marker.is_file(), "Run sudo deploy/scripts/smoke-coffee-import.sh --source roast.by first"
expected = fingerprint("/etc/coffeepeek/roaster-mapping.json", "/etc/coffeepeek/coffee-import.env", sys.argv[1] + "/dev/coffee-import/requirements.txt")
assert json.loads(marker.read_text())["fingerprint"] == expected, "Configuration changed after the smoke test; run it again"
PY
systemctl enable --now coffeepeek-coffee-import.timer
systemctl list-timers coffeepeek-coffee-import.timer
