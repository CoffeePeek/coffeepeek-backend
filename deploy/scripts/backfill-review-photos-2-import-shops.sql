-- =============================================================================
-- Backfill review photos, STEP 2 of 2: import into the SHOPS database.
-- =============================================================================
--
-- Purpose
--   Inserts the photos exported by step 1 into Shops "ShopPhotos", attached to
--   the published "Reviews" row whose "ModerationReviewId" matches.
--
--   Idempotent and safe to re-run: a review is only filled when it currently has
--   ZERO photos (NOT EXISTS on "ReviewId"), so a second run inserts nothing and
--   reviews already fixed by the new handler are left alone. Everything runs in
--   one transaction with ON_ERROR_STOP, so a failure leaves no partial state.
--
--   "Reviews" is never updated: "IsSoftDelete" is not touched, so user-deleted
--   or edit-hidden reviews stay hidden. Soft-deleted reviews do get their photos
--   (harmless and invisible; they show up if a later re-approval restores them).
--   No events are republished.
--
-- Run (from the deploy/ directory, AFTER step 1):
--   docker compose exec -T postgres psql -U "$POSTGRES_USER" -d cpmoderationdb -v ON_ERROR_STOP=1 -f - < scripts/backfill-review-photos-1-export-moderation.sql
--   docker compose exec -T postgres psql -U "$POSTGRES_USER" -d cpshopsdb      -v ON_ERROR_STOP=1 -f - < scripts/backfill-review-photos-2-import-shops.sql
--
--   psql runs INSIDE the postgres container, so \copy reads the CSV from the
--   container filesystem: /tmp/review_photos_backfill.csv. Remove it afterwards:
--   docker compose exec postgres rm -f /tmp/review_photos_backfill.csv
--
-- Local (Aspire) variant: psql "<shops connection string>" -v ON_ERROR_STOP=1 -f deploy/scripts/backfill-review-photos-2-import-shops.sql
--
-- Dry run: replace the final COMMIT with ROLLBACK.
-- gen_random_uuid() is built into PostgreSQL 13+ (PG17 here), no extension needed.
-- =============================================================================

BEGIN;

CREATE TEMP TABLE review_photo_import (
    moderation_review_id uuid,
    file_name            text,
    content_type         text,
    storage_key          text,
    size_bytes           bigint,
    owner_id             uuid,
    sort_index           int
) ON COMMIT DROP;

\copy review_photo_import FROM '/tmp/review_photos_backfill.csv' WITH CSV HEADER

SELECT count(*) AS imported_csv_rows FROM review_photo_import;

-- Rows that will be SKIPPED because they exceed the Shops column limits
-- (FileName varchar(50), ContentType varchar(30), StorageKey varchar(200)).
SELECT moderation_review_id, file_name, content_type, storage_key,
       length(file_name) AS file_name_len,
       length(content_type) AS content_type_len,
       length(storage_key) AS storage_key_len
FROM review_photo_import
WHERE length(file_name) > 50
   OR length(content_type) > 30
   OR length(storage_key) > 200;

WITH inserted AS (
    INSERT INTO "ShopPhotos" ("Id", "FileName", "ContentType", "StorageKey", "SizeBytes", "OwnerId", "SortIndex", "ReviewId", "CreatedAtUtc", "UpdatedAtUtc")
    SELECT gen_random_uuid(), i.file_name, i.content_type, i.storage_key, i.size_bytes, i.owner_id, i.sort_index, rv."Id", now(), NULL
    FROM review_photo_import i
    JOIN "Reviews" rv ON rv."ModerationReviewId" = i.moderation_review_id
    WHERE NOT EXISTS (SELECT 1 FROM "ShopPhotos" sp WHERE sp."ReviewId" = rv."Id")
      AND i.owner_id IS NOT NULL
      AND length(i.file_name) <= 50
      AND length(i.content_type) <= 30
      AND length(i.storage_key) <= 200
    RETURNING "ReviewId"
)
SELECT count(*) AS inserted_photos,
       count(DISTINCT "ReviewId") AS reviews_filled
FROM inserted;

SELECT count(DISTINCT "ReviewId") AS reviews_with_photos_total
FROM "ShopPhotos"
WHERE "ReviewId" IS NOT NULL;

COMMIT;
