#!/usr/bin/env bash
# Import one real source through the regular unit and verify PostgreSQL + MinIO.
set -euo pipefail
if [[ "${EUID}" != "0" || "${1:-}" != "--source" || -z "${2:-}" || "$#" != "2" ]]; then
  echo "Usage: sudo deploy/scripts/smoke-coffee-import.sh --source roast.by" >&2
  exit 1
fi
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
IMPORT_PYTHON=/opt/coffeepeek-coffee-import/.venv/bin/python
set -a
# shellcheck disable=SC1091
source "${REPO_ROOT}/deploy/.env"
set +a
bash "${REPO_ROOT}/deploy/scripts/check-coffee-import.sh"
"${IMPORT_PYTHON}" "${REPO_ROOT}/dev/coffee-import/validate_deployment.py" --mapping "${COFFEE_IMPORT_MAPPING_FILE}" --job-env /etc/coffeepeek/coffee-import.env
source_name="$2"
source_key="$("${IMPORT_PYTHON}" -c 'import sys; sys.path.insert(0, sys.argv[1]); from validate_deployment import selected_sources; print(selected_sources(sys.argv[2])[0])' "${REPO_ROOT}/dev/coffee-import" "${source_name}")"
"${IMPORT_PYTHON}" -c 'import sys; sys.path.insert(0, sys.argv[1]); from validate_deployment import validate_mapping; assert sys.argv[3] in validate_mapping(sys.argv[2]), "Selected source is not mapped"' "${REPO_ROOT}/dev/coffee-import" "${COFFEE_IMPORT_MAPPING_FILE}" "${source_key}"
if systemctl is-active --quiet coffeepeek-coffee-import.timer || systemctl is-active --quiet coffeepeek-coffee-import.service; then
  echo "Stop the import timer and service before running the single-source smoke test." >&2
  exit 1
fi

cd "${REPO_ROOT}/deploy"
rm -f /etc/coffeepeek/coffee-import-smoke.json
work="$(mktemp -d)"
cleanup() {
  rm -f /run/coffeepeek/coffee-import-override.env
  rm -rf "${work}"
}
trap cleanup EXIT
install -d -m 0755 /run/coffeepeek
printf 'COFFEE_IMPORT_SOURCES="%s"\n' "${source_name}" > /run/coffeepeek/coffee-import-override.env
chmod 0600 /run/coffeepeek/coffee-import-override.env

sql() {
  docker compose exec -T postgres psql -X -v ON_ERROR_STOP=1 -U "${POSTGRES_USER}" -d "$1" --csv -t -c "$2"
}
product_query="SELECT s.\"ExternalProductId\", p.\"Id\", p.\"Status\", array_to_json(p.\"ProtectedFields\") FROM \"CoffeeProductSources\" s JOIN \"CoffeeProducts\" p ON p.\"Id\"=s.\"CoffeeProductId\" WHERE s.\"SourceKey\"='${source_key}' ORDER BY p.\"Id\""
sql cpshopsdb "${product_query}" > "${work}/before.csv"
# Source keys and GUIDs were validated before interpolation; only observed source names are allowed.
"${IMPORT_PYTHON}" - "${COFFEE_IMPORT_MAPPING_FILE}" "${source_key}" > "${work}/roasters.sql" <<'PY'
import json
import sys
import uuid
mapping = json.load(open(sys.argv[1]))["CoffeeImportOptions"]["Roasters"][sys.argv[2]]
ids = sorted({str(uuid.UUID(value)) for value in mapping.values()})
quoted = ",".join("'" + value + "'" for value in ids)
print(f'SELECT COUNT(*) FROM "Roasters" WHERE "Id" IN ({quoted})')
PY
actual="$(sql cpshopsdb "$(cat "${work}/roasters.sql")")"
expected="$("${IMPORT_PYTHON}" -c 'import json,sys; print(len(set(json.load(open(sys.argv[1]))["CoffeeImportOptions"]["Roasters"][sys.argv[2]].values())))' "${COFFEE_IMPORT_MAPPING_FILE}" "${source_key}")"
if [[ "${actual}" != "${expected}" ]]; then
  echo "The mapping references a roaster absent from PostgreSQL." >&2
  exit 1
fi
systemctl start coffeepeek-coffee-import.service
sql cpshopsdb "${product_query}" > "${work}/after.csv"
sql cpshopsdb "SELECT \"ExternalVariantId\",\"Price\",\"Currency\",\"WeightGrams\",\"Availability\" FROM \"CoffeeVariants\" WHERE \"SourceKey\"='${source_key}'" > "${work}/variants.csv"
sql cpshopsdb "SELECT s.\"ExternalProductId\",p.\"StorageKey\",p.\"MediaPhotoId\" FROM \"CoffeeProductPhotos\" p JOIN \"CoffeeProductSources\" s ON s.\"Id\"=p.\"CoffeeProductSourceId\" WHERE s.\"SourceKey\"='${source_key}'" > "${work}/photos.csv"
sql cpmediadb 'SELECT "StorageKey","Id" FROM "Photos" WHERE "StorageKey" LIKE '\''coffees/%'\''' > "${work}/media-photos.csv"
COFFEE_IMPORT_SHOPS_URL="$("${IMPORT_PYTHON}" -c 'import sys; sys.path.insert(0, sys.argv[1]); from validate_deployment import read_env; print(read_env(sys.argv[2])["COFFEE_IMPORT_SHOPS_URL"])' "${REPO_ROOT}/dev/coffee-import" /etc/coffeepeek/coffee-import.env)"
COFFEE_IMPORT_MEDIA_URL="$("${IMPORT_PYTHON}" -c 'import sys; sys.path.insert(0, sys.argv[1]); from validate_deployment import read_env; print(read_env(sys.argv[2])["COFFEE_IMPORT_MEDIA_URL"])' "${REPO_ROOT}/dev/coffee-import" /etc/coffeepeek/coffee-import.env)"
export COFFEE_IMPORT_SHOPS_URL COFFEE_IMPORT_MEDIA_URL
"${IMPORT_PYTHON}" "${REPO_ROOT}/dev/coffee-import/verify_deployment_smoke.py" --state /var/lib/coffeepeek-coffee-import --source "${source_key}" --rows "${work}"
storage_key="$("${IMPORT_PYTHON}" -c 'import json,sys; print(json.load(open(sys.argv[1]))["storageKey"])' "${work}/probe.json")"
docker compose exec -T media curl --fail --silent --show-error --max-time 30 \
  "http://minio:9000/coffeepeek.shops/${storage_key}" > "${work}/probe.bin"
"${IMPORT_PYTHON}" - "${work}" <<'PY'
import hashlib
import json
import sys
from pathlib import Path
work = Path(sys.argv[1])
probe = json.loads((work / "probe.json").read_text())
data = (work / "probe.bin").read_bytes()
assert len(data) == probe["sizeBytes"], "MinIO image length differs from Media metadata"
assert hashlib.sha256(data).hexdigest() == probe["storageKey"].split("/")[1].split(".")[0], "MinIO image content differs from its storage hash"
print("MinIO uploaded bytes verified")
PY
"${IMPORT_PYTHON}" - "${REPO_ROOT}" "${source_key}" <<'PY'
import json
import sys
from datetime import datetime, timezone
from pathlib import Path
sys.path.insert(0, sys.argv[1] + "/dev/coffee-import")
from verify_deployment_smoke import fingerprint
record = {"source": sys.argv[2], "checkedAtUtc": datetime.now(timezone.utc).isoformat(),
          "fingerprint": fingerprint("/etc/coffeepeek/roaster-mapping.json", "/etc/coffeepeek/coffee-import.env", sys.argv[1] + "/dev/coffee-import/requirements.txt")}
path = Path("/etc/coffeepeek/coffee-import-smoke.json")
path.write_text(json.dumps(record) + "\n")
path.chmod(0o600)
PY
echo "Single-source PostgreSQL/MinIO smoke passed. New products remain Draft. The timer is still disabled."
