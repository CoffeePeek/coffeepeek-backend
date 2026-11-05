-- One-time publication of every existing shelf, including explicitly hidden shelves.
-- Run against cpshopsdb after migrations, with shelf writers stopped.
-- Repeating this script publishes shelves hidden again since its previous run.
BEGIN;
SET LOCAL lock_timeout = '30s';
LOCK TABLE "ShelfBags", "ShelfProfilePreferences" IN SHARE ROW EXCLUSIVE MODE;

WITH owners AS (
    SELECT "UserId" AS "OwnerId" FROM "ShelfBags"
    UNION
    SELECT "OwnerId" FROM "ShelfProfilePreferences"
), published AS (
    INSERT INTO "ShelfProfilePreferences" ("OwnerId", "IsVisible")
    SELECT "OwnerId", true FROM owners WHERE true
    ON CONFLICT ("OwnerId") DO UPDATE
    SET "IsVisible" = EXCLUDED."IsVisible"
    WHERE NOT "ShelfProfilePreferences"."IsVisible"
    RETURNING "OwnerId"
)
SELECT count(*) AS "publishedShelfCount" FROM published;

COMMIT;
