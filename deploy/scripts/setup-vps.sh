#!/usr/bin/env bash
# Bootstrap a fresh Ubuntu/Debian VPS for CoffeePeek Docker deployment.
set -euo pipefail

if [[ "${EUID}" -ne 0 ]]; then
  echo "Run as root: sudo $0"
  exit 1
fi

export DEBIAN_FRONTEND=noninteractive

apt-get update
apt-get install -y ca-certificates curl git ufw python3 python3-venv openssl

if ! command -v docker >/dev/null 2>&1; then
  curl -fsSL https://get.docker.com | sh
fi

systemctl enable docker
systemctl start docker

if ! docker compose version >/dev/null 2>&1; then
  apt-get install -y docker-compose-plugin
fi

ufw allow OpenSSH
ufw allow 80/tcp
ufw allow 443/tcp
ufw --force enable

DEPLOY_DIR="${DEPLOY_DIR:-/opt/coffeepeek}"
mkdir -p "${DEPLOY_DIR}"

cat <<EOF

VPS bootstrap complete.

Next steps:
  1. Clone repo to ${DEPLOY_DIR} (or copy deploy/ folder)
  2. cd ${DEPLOY_DIR}/deploy && cp .env.example .env && nano .env
  3. ./scripts/deploy.sh

EOF
