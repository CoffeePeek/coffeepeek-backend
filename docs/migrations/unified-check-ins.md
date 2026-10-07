# Canonical check-ins: transfer and remove legacy data

Runtime uses `/api/v1/check-ins` and `/api/v1/moderation/check-ins`. The earlier additive migrations introduced the canonical schema and left physical archives. The forward cleanup now transfers recoverable records and permanently removes those archives, as explicitly requested. No backups are created.

`CoffeePeek.LegacyCheckInMigration` coordinates all four databases and MinIO before EF cleanup:

- A visit requires an existing active author, an existing shop, nonblank text of at most 1000 characters and coffee/service/place scores in 1–5. Missing or invalid values are never fabricated. Invalid records, removed reviews and orphaned moderation are discarded.
- A linked review can complete an incomplete visit only when author and shop match. A complete visit with different text, scores or frozen drink fields is retained separately. Matching records preserve the visit ID and visit time; standalone reviews use their existing review ID and creation time.
- Visits become private by default. A matching legacy moderation snapshot preserves pending/rejected status; public approval is preserved only for an exact match of text, scores, frozen drink fields and the successfully transferred photo set. A changed or missing photo resets the record to private/unsubmitted, allowing a new submission.
- Up to five owned, confirmed JPEG/PNG/GIF/WebP/BMP/AVIF photos are verified and copied into the private check-in bucket. Media and Shops use the same photo IDs. Metadata ownership and actual raster signatures are checked; missing/invalid bytes are discarded. Storage/network errors stop the run for retry.
- Original legacy photo objects and metadata are removed after verified copying. Old public object/version locators are explicitly denied. Keys still used by shop galleries, menus, roasters or catalog coffee remain available to those entities; check-ins receive independent private copies.
- Helpful votes transfer once, excluding self-votes and missing users; counts are recalculated. Complaints move to the mapped canonical target; complaints about discarded reviews are removed. Existing canonical content takes precedence over colliding legacy IDs. Account check-in counters are recalculated from canonical created visits.

| Database | Forward cleanup |
| --- | --- |
| Shops | `20261007160000_RemoveLegacyCheckInArchives` removes `CheckIns`, `Reviews`, `ReviewHelpfulVotes`, obsolete photo rows and `ShopPhotos.CheckInId/ReviewId`. Canonical check-ins, helpful votes, complaint audit and shared galleries remain. Shop completeness calculation and its visit trigger now use canonical current-revision data. |
| Moderation | `20261007160000_RemoveLegacyReviewSnapshots` removes `ModerationReviews`, obsolete snapshot photos and `ShopPhotos.ModerationReviewId`. Canonical snapshots and current shop/roaster moderation remain. |
| Account | `20261007160000_RemoveLegacyReviewCounter` removes `Users.ReviewCount` after recalculating `CheckInCount`. |
| Media | Current schema stays intact; the runner removes scoped obsolete photo records/objects and creates private canonical claims. |

Historical migrations and model snapshots are retained so fresh installs and upgrades still work. No legacy compatibility APIs or runtime writers are restored. Cleanup is forward-only: `Down` throws because discarded records cannot be reconstructed.

## Deployment

`deploy/scripts/ci-deploy.sh` stops gateway/account/shops/moderation/media when legacy tables remain, runs `apply-migrations.sh`, then restarts the new services. An unsuccessful migration leaves application writers stopped; fix the cause and rerun the same deployment. No backup/export phase exists. The runner serializes concurrent executions with a dedicated PostgreSQL session lock.

`apply-migrations.sh` builds and runs the console tool. It first upgrades each database to the existing canonical migrations, transfers data and private objects, writes completion flags containing no archived content, then applies cleanup. Nonempty archives cannot be dropped by a direct `dotnet ef database update` before transfer completes. Completion flags are removed with cleanup. Retry works after interrupted transfer or partial schema cleanup; a later deployment never downgrades the cleaned schema.

The runner reads `PG_HOST`, `PG_PORT`, `POSTGRES_USER`, `POSTGRES_PASSWORD`, `MINIO_ROOT_USER`, `MINIO_ROOT_PASSWORD` and optional `MinIOOptions__*`. Production uses the internal MinIO address supplied by CI deployment. Optional `LegacyMigration__Account/Shops/Moderation/Media` connection strings are available for isolated verification. Credentials, object keys and user text are never logged.

For development, stop running services and execute `dev/migrate-and-seed.sh`. Direct execution against configured databases: `dotnet run --project CoffeePeek.LegacyCheckInMigration --configuration Release`.

## Verification

`CoffeePeek.LegacyCheckInMigration.Tests` covers mapping rules and rehearses the real four-database EF history on PostgreSQL 17, including incomplete visits, snapshot approval, helpful/report transfer, counters, cleanup refusal before transfer, a simulated storage failure, retries and complete archive removal. Photo scenarios include changed moderation photos and keys shared with a live gallery.

Set `COFFEEPEEK_LIVE_PG` and `PGPASSFILE` as described in [the live PostgreSQL guide](../api/public-addresses.md#live-postgresql-gate). Adding `COFFEEPEEK_LIVE_MINIO=http://127.0.0.1:9000` exercises actual byte copying, public source denial and private bucket access on an isolated loopback MinIO with disposable test credentials `coffee-legacy-test` / `coffee-legacy-test-only`; only uniquely named `cp-legacy-*` buckets are created and removed.

These checks use disposable infrastructure. Applying this migration to the VPS is a separate deployment; local verification does not claim production data has already moved.
