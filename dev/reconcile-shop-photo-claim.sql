-- Operator-only recovery for ONE Media claim. Read docs/runbooks/shops-photo-claims.md first.
-- Required psql variables: photo_id, owner_id, shop_id, kind, storage_key,
-- claimed_at_utc, and attestation (exactly SHOPS_AND_MEDIA_STOPPED_ABSENCE_VERIFIED).
-- psql -X -v ON_ERROR_STOP=1 -v photo_id=... -v ... -f dev/reconcile-shop-photo-claim.sql

BEGIN ISOLATION LEVEL SERIALIZABLE;
SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '30s';

CREATE TEMP TABLE reconcile_expected (
    photo_id uuid NOT NULL,
    owner_id uuid NOT NULL,
    shop_id uuid NOT NULL,
    kind varchar(20) NOT NULL,
    storage_key varchar(255) NOT NULL,
    claimed_at_utc timestamptz NOT NULL,
    attestation text NOT NULL
) ON COMMIT DROP;

INSERT INTO reconcile_expected VALUES (
    :'photo_id'::uuid,
    :'owner_id'::uuid,
    :'shop_id'::uuid,
    :'kind',
    :'storage_key',
    :'claimed_at_utc'::timestamptz,
    :'attestation'
);

DO $$
BEGIN
    IF (SELECT attestation FROM reconcile_expected) <>
       'SHOPS_AND_MEDIA_STOPPED_ABSENCE_VERIFIED' THEN
        RAISE EXCEPTION 'Missing operator attestation; no claim was released.';
    END IF;
    IF (SELECT kind FROM reconcile_expected) NOT IN ('Gallery', 'Menu', 'Roaster') THEN
        RAISE EXCEPTION 'Only Gallery, Menu and Roaster claims can be reconciled here.';
    END IF;
END
$$;

-- The row lock excludes a concurrent change even if the runbook's stop step was missed.
SELECT p."Id", p."OwnerId", p."StorageKey", p."ShopAttachmentId",
       p."ShopAttachmentKind", p."ClaimedAtUtc", p."Status"
FROM "Photos" AS p
JOIN reconcile_expected AS e ON p."Id" = e.photo_id
FOR UPDATE OF p;

CREATE TEMP TABLE reconcile_released (photo_id uuid NOT NULL) ON COMMIT DROP;

WITH released AS (
    UPDATE "Photos" AS p
       SET "ShopAttachmentId" = NULL,
           "ShopAttachmentKind" = NULL,
           "ClaimedAtUtc" = NULL
      FROM reconcile_expected AS e
     WHERE p."Id" = e.photo_id
       AND p."OwnerId" = e.owner_id
       AND p."StorageKey" = e.storage_key
       AND p."ShopAttachmentId" = e.shop_id
       AND p."ShopAttachmentKind" = e.kind
       AND p."ClaimedAtUtc" = e.claimed_at_utc
       AND p."ClaimedAtUtc" <= clock_timestamp() - interval '48 hours'
       AND p."Status" = 'Pending'
       AND p."BucketType" = 'Shop'
       AND p."CheckInId" IS NULL
    RETURNING p."Id"
)
INSERT INTO reconcile_released SELECT "Id" FROM released;

DO $$
BEGIN
    IF (SELECT count(*) FROM reconcile_released) <> 1 THEN
        RAISE EXCEPTION 'Claim differs from reviewed snapshot or is not eligible; transaction rolled back.';
    END IF;
END
$$;

COMMIT;
