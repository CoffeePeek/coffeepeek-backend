#!/bin/sh
# Runs in minio/mc. Provisioning must succeed before Media starts.
set -eu
: "${MINIO_ROOT_USER:?required}" "${MINIO_ROOT_PASSWORD:?required}"
: "${MINIO_CHECKIN_READ_USER:?required}" "${MINIO_CHECKIN_READ_PASSWORD:?required}"
if [ "$MINIO_CHECKIN_READ_USER" = "$MINIO_ROOT_USER" ]; then
    echo 'Check-in reader must use a separate MinIO identity.' >&2
    exit 1
fi

attempt=0
until mc alias set local http://minio:9000 "$MINIO_ROOT_USER" "$MINIO_ROOT_PASSWORD" >/dev/null 2>&1; do
    attempt=$((attempt + 1))
    [ "$attempt" -lt 30 ] || { echo 'MinIO is unavailable.' >&2; exit 1; }
    sleep 2
done
mc mb --ignore-existing local/coffeepeek.shops
mc mb --ignore-existing local/coffeepeek.avatars
mc mb --ignore-existing local/coffeepeek.checkins
mc anonymous set none local/coffeepeek.checkins
# Preserve historical per-object denies while keeping the existing public gallery policy.
if ! mc anonymous get-json local/coffeepeek.shops | grep -q CheckInPrivate; then
    mc anonymous set download local/coffeepeek.shops
fi
mc anonymous set download local/coffeepeek.avatars

mc admin policy create local coffeepeek-checkin-reader /provision/checkin-reader-policy.json
mc admin user add local "$MINIO_CHECKIN_READ_USER" "$MINIO_CHECKIN_READ_PASSWORD" >/dev/null
mc admin policy attach local coffeepeek-checkin-reader --user "$MINIO_CHECKIN_READ_USER"
