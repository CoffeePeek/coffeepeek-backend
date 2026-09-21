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
