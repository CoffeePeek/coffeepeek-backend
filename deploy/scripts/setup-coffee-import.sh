#!/usr/bin/env bash
# Install/update the permanent collector runtime and units, without enabling the timer.
set -euo pipefail
umask 022
if [[ "${EUID}" != "0" ]]; then
  echo "Run setup as root (sudo); it installs /etc configuration and systemd units." >&2
  exit 1
fi
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
if [[ "${REPO_ROOT}" != "/opt/coffeepeek" ]]; then
  echo "The shipped unit expects /opt/coffeepeek; install the checkout there before setup." >&2
  exit 1
fi
if [[ "${1:-}" != "--mapping" || -z "${2:-}" || "$#" != "2" ]]; then
  echo "Usage: sudo deploy/scripts/setup-coffee-import.sh --mapping <reviewed-roaster-mapping.json>" >&2
  exit 1
fi
mapping="$(realpath "$2")"
python3 "${REPO_ROOT}/dev/coffee-import/validate_deployment.py" --mapping "${mapping}" --mapping-only
if systemctl is-active --quiet coffeepeek-coffee-import.service || systemctl is-active --quiet coffeepeek-coffee-import.timer; then
  echo "Stop the coffee import timer and service before updating their configuration/runtime." >&2
  exit 1
fi

set -a
# shellcheck disable=SC1091
source "${REPO_ROOT}/deploy/.env"
set +a
if [[ -z "${COFFEE_IMPORT_TOKEN:-}" ]]; then
  COFFEE_IMPORT_TOKEN="$(openssl rand -hex 32)"
fi
export COFFEE_IMPORT_TOKEN
python3 -c 'import sys; sys.path.insert(0, sys.argv[1]); from validate_deployment import validate_token; import os; validate_token(os.environ["COFFEE_IMPORT_TOKEN"])' "${REPO_ROOT}/dev/coffee-import"

runtime=/opt/coffeepeek-coffee-import
install -d -m 0755 "${runtime}" /etc/coffeepeek
python3 -m venv "${runtime}/.venv"
"${runtime}/.venv/bin/python" -m pip install -r "${REPO_ROOT}/dev/coffee-import/requirements.txt"
chmod -R a+rX "${runtime}/.venv"
sha256sum "${REPO_ROOT}/dev/coffee-import/requirements.txt" | cut -d ' ' -f1 > "${runtime}/requirements.sha256"
if [[ "${mapping}" != "/etc/coffeepeek/roaster-mapping.json" ]]; then
  install -m 0644 "${mapping}" /etc/coffeepeek/roaster-mapping.json
fi
chmod 0644 /etc/coffeepeek/roaster-mapping.json
COFFEE_IMPORT_MAPPING_FILE=/etc/coffeepeek/roaster-mapping.json
export COFFEE_IMPORT_MAPPING_FILE
COFFEE_IMPORT_SOURCES="$(python3 "${REPO_ROOT}/dev/coffee-import/validate_deployment.py" --mapping "${COFFEE_IMPORT_MAPPING_FILE}" --print-sources)"
export COFFEE_IMPORT_SOURCES
export COFFEE_IMPORT_SHOPS_URL="http://127.0.0.1:${COFFEE_IMPORT_SHOPS_PORT:-5243}"
export COFFEE_IMPORT_MEDIA_URL="http://127.0.0.1:${COFFEE_IMPORT_MEDIA_PORT:-5025}"

python3 - "${REPO_ROOT}/deploy/.env" <<'PY'
import json
import os
import shlex
import sys
from pathlib import Path
env = Path(sys.argv[1])
updated = {name: os.environ[name] for name in ("COFFEE_IMPORT_TOKEN", "COFFEE_IMPORT_MAPPING_FILE")}
lines = [line for line in env.read_text().splitlines() if line.partition("=")[0] not in updated]
env.write_text("\n".join(lines + [f"{key}={shlex.quote(value)}" for key, value in updated.items()]) + "\n")
env.chmod(0o600)
job = Path("/etc/coffeepeek/coffee-import.env")
job.write_text("\n".join(f"{key}={json.dumps(os.environ[key], ensure_ascii=False)}" for key in (
    "COFFEE_IMPORT_SHOPS_URL", "COFFEE_IMPORT_MEDIA_URL", "COFFEE_IMPORT_TOKEN", "COFFEE_IMPORT_SOURCES")) + "\n")
job.chmod(0o600)
Path("/etc/coffeepeek/coffee-import-smoke.json").unlink(missing_ok=True)
PY
python3 "${REPO_ROOT}/dev/coffee-import/validate_deployment.py" --mapping "${COFFEE_IMPORT_MAPPING_FILE}" --job-env /etc/coffeepeek/coffee-import.env
install -m 0644 "${REPO_ROOT}"/deploy/systemd/coffeepeek-coffee-import.{service,timer} /etc/systemd/system/
systemctl daemon-reload
systemctl disable coffeepeek-coffee-import.timer
echo "Runtime and units installed. The timer is disabled. Deploy/recreate Shops and Media, then run the single-source smoke test."
