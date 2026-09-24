-- =============================================================================
-- Backfill review photos, STEP 1 of 2: export from the MODERATION database.
-- =============================================================================
--
-- Purpose
--   Before quick-260924-mii, ModerationReviewApprovedHandler (Shops) ignored the
--   photos on an approved moderation review, so every published review had
--   photos: []. This step exports the photo metadata of every APPROVED moderation
--   review to a CSV. Step 2 imports it into the Shops database.
--
--   Read-only against the moderation DB; idempotent and safe to re-run (it just
--   overwrites the CSV). No events are republished: a republish would make Shops
--   call Restore() and bring back reviews that users deleted.
--
-- Run (from the deploy/ directory, VPS / docker compose):
--   docker compose exec -T postgres psql -U "$POSTGRES_USER" -d cpmoderationdb -v ON_ERROR_STOP=1 -f - < scripts/backfill-review-photos-1-export-moderation.sql
--   docker compose exec -T postgres psql -U "$POSTGRES_USER" -d cpshopsdb      -v ON_ERROR_STOP=1 -f - < scripts/backfill-review-photos-2-import-shops.sql
--
--   psql runs INSIDE the postgres container, so \copy writes the CSV to the
--   container filesystem: /tmp/review_photos_backfill.csv. Remove it afterwards:
--   docker compose exec postgres rm -f /tmp/review_photos_backfill.csv
--
-- Local (Aspire) variant: run psql on your machine with the connection string
-- shown in the Aspire dashboard; the CSV is then written to your local /tmp:
--   psql "<moderation connection string>" -v ON_ERROR_STOP=1 -f deploy/scripts/backfill-review-photos-1-export-moderation.sql
--   psql "<shops connection string>"      -v ON_ERROR_STOP=1 -f deploy/scripts/backfill-review-photos-2-import-shops.sql
--
-- Ordering: moderation "ShopPhotos" has no sort column, so sort_index is the
-- 0-based upload order (CreatedAtUtc, then Id as a stable tiebreaker).
-- owner_id falls back to the review author when the photo has no OwnerId.
-- =============================================================================

-- \copy requires the whole query on ONE line.
\copy (SELECT p."ModerationReviewId" AS moderation_review_id, p."FileName" AS file_name, p."ContentType" AS content_type, p."StorageKey" AS storage_key, p."SizeBytes" AS size_bytes, COALESCE(p."OwnerId", r."UserId") AS owner_id, (ROW_NUMBER() OVER (PARTITION BY p."ModerationReviewId" ORDER BY p."CreatedAtUtc", p."Id") - 1) AS sort_index FROM "ShopPhotos" p JOIN "ModerationReviews" r ON r."Id" = p."ModerationReviewId" WHERE r."ModerationStatus" = 1) TO '/tmp/review_photos_backfill.csv' WITH CSV HEADER

-- How many photo rows were exported (same filter as above).
SELECT count(*) AS exported_photo_rows,
       count(DISTINCT p."ModerationReviewId") AS exported_reviews
FROM "ShopPhotos" p
JOIN "ModerationReviews" r ON r."Id" = p."ModerationReviewId"
WHERE r."ModerationStatus" = 1;
