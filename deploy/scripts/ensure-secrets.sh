#!/usr/bin/env bash
# Persist newly required secrets in deploy/.env without overwriting existing values.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ENV_FILE="$(cd "${SCRIPT_DIR}/.." && pwd)/.env"

if [[ ! -f "${ENV_FILE}" ]]; then
  echo "Missing ${ENV_FILE}" >&2
  exit 1
fi

current_value() {
  local key="$1"
  sed -n "s/^${key}=//p" "${ENV_FILE}" | tail -n1
}

upsert_if_empty() {
  local key="$1"
  local value="$2"
  if grep -q "^${key}=" "${ENV_FILE}"; then
    if [[ -n "$(current_value "${key}")" ]]; then
      return
    fi
    sed -i "s|^${key}=.*|${key}=${value}|" "${ENV_FILE}"
  else
    printf '\n%s=%s\n' "${key}" "${value}" >> "${ENV_FILE}"
  fi
}

set -a
# shellcheck disable=SC1091
source "${ENV_FILE}"
set +a

pepper="${ACCOUNT_DELETION_TOKEN_PEPPER:-}"
if [[ -z "${pepper}" ]]; then
  if command -v openssl >/dev/null 2>&1; then
    pepper="$(openssl rand -hex 32)"
  else
    pepper="${JWT_SECRET_KEY:?JWT_SECRET_KEY is required to seed ACCOUNT_DELETION_TOKEN_PEPPER}"
  fi
  echo "==> Writing ACCOUNT_DELETION_TOKEN_PEPPER to .env"
fi

upsert_if_empty ACCOUNT_DELETION_TOKEN_PEPPER "${pepper}"

# Shared secret between the gateway and downstream services (X-Gateway-Auth). Services refuse the
# committed development key outside Development, so every deployment needs its own value.
gateway_secret="${GATEWAY_AUTH_SECRET:-}"
if [[ -z "${gateway_secret}" ]]; then
  if ! command -v openssl >/dev/null 2>&1; then
    echo "openssl is required to generate GATEWAY_AUTH_SECRET (or set it in ${ENV_FILE})" >&2
    exit 1
  fi
  gateway_secret="$(openssl rand -hex 32)"
  echo "==> Writing GATEWAY_AUTH_SECRET to .env"
fi

upsert_if_empty GATEWAY_AUTH_SECRET "${gateway_secret}"

# GET photo requests use a dedicated read-only MinIO identity. Preserve it across upgrades.
photo_reader_user="${MINIO_CHECKIN_READ_USER:-coffeepeek-checkin-reader}"
if [[ "${photo_reader_user}" == "${MINIO_ROOT_USER:-}" ]]; then
  echo "MINIO_CHECKIN_READ_USER must differ from MINIO_ROOT_USER" >&2
  exit 1
fi
photo_reader_password="${MINIO_CHECKIN_READ_PASSWORD:-}"
if [[ -z "${photo_reader_password}" ]]; then
  if ! command -v openssl >/dev/null 2>&1; then
    echo "openssl is required to generate MINIO_CHECKIN_READ_PASSWORD" >&2
    exit 1
  fi
  photo_reader_password="$(openssl rand -hex 32)"
  echo "==> Writing MINIO_CHECKIN_READ_PASSWORD to .env"
fi
upsert_if_empty MINIO_CHECKIN_READ_USER "${photo_reader_user}"
upsert_if_empty MINIO_CHECKIN_READ_PASSWORD "${photo_reader_password}"

variant_user="${MINIO_CHECKIN_VARIANT_USER:-coffeepeek-checkin-variant-writer}"
if [[ "${variant_user}" == "${MINIO_ROOT_USER:-}" || "${variant_user}" == "${photo_reader_user}" ]]; then
  echo "Variant writer must use its own MinIO identity" >&2
  exit 1
fi
upsert_if_empty MINIO_CHECKIN_VARIANT_USER "${variant_user}"
for key in MINIO_CHECKIN_VARIANT_PASSWORD CHECKIN_RENDERER_KEY CHECKIN_RENDERER_SALT; do
  if [[ -z "$(current_value "${key}")" ]]; then
    command -v openssl >/dev/null 2>&1 || { echo "openssl is required to generate ${key}" >&2; exit 1; }
    upsert_if_empty "${key}" "$(openssl rand -hex 32)"
  fi
done

variant_reader_user="${MINIO_CHECKIN_VARIANT_READ_USER:-coffeepeek-checkin-variant-reader}"
if [[ "${variant_reader_user}" == "${MINIO_ROOT_USER:-}" || "${variant_reader_user}" == "${photo_reader_user}" || "${variant_reader_user}" == "${variant_user}" ]]; then
  echo "Variant reader must use its own MinIO identity" >&2
  exit 1
fi
upsert_if_empty MINIO_CHECKIN_VARIANT_READ_USER "${variant_reader_user}"
if [[ -z "$(current_value MINIO_CHECKIN_VARIANT_READ_PASSWORD)" ]]; then
  command -v openssl >/dev/null 2>&1 || { echo "openssl is required to generate variant reader password" >&2; exit 1; }
  upsert_if_empty MINIO_CHECKIN_VARIANT_READ_PASSWORD "$(openssl rand -hex 32)"
fi
