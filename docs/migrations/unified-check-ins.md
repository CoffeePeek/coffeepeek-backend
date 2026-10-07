# Canonical check-ins: additive release with retained archives

Runtime uses `/api/v1/check-ins` and `/api/v1/moderation/check-ins`. Standalone reviews, compatibility APIs/events and old writers are retired. Historical main migrations/designers remain unchanged; new forward migrations prepare source only, with no live database apply.

| Context / migration | Supported schema and archive |
| --- | --- |
| Shops / `20261007133435_CanonicalCheckInsOnMain` | Creates fresh `CanonicalCheckIns` and `CheckInHelpfulVotes`; adds `ShopPhotos.CanonicalCheckInId`. Old `CheckIns`, `Reviews`, `ReviewHelpfulVotes`, data and photo associations remain physical archives outside runtime. |
| Moderation / `20261007133453_CanonicalCheckInSnapshotsOnMain` | Creates fresh `ModerationCheckIns` with immutable frozen drink names and unique visit/revision. Adds separate `ShopPhotos.ModerationCheckInId`, photo ID and order; existing snapshot/photo history is untouched. |
| Account / `RetireReviewCountOnMain` | Runtime omits the old review counter; physical values remain archived with default zero so registration satisfies the old NOT NULL column. Useful account counters remain. |
| Media / `CanonicalCheckInPhotoClaimsOnMain` | Adds claim parent/time/original-key metadata/index; preserves storage-key uniqueness, coffee imports and all objects. |

No legacy rows/tables/columns are deleted, renamed or copied into canonical runtime. Fresh canonical tables start empty; old tester history is preserved without publication. Existing DbSet/type names may map to the new physical tables. Shared galleries, menus, roasters and unrelated audit data remain intact.

The existing `ReviewReports` table/target column and all complaint/admin audit remain. Only its obsolete FK to archived Reviews is detached so canonical complaints can be inserted. Old uncertain targets have no canonical content projection. No guessed cross-database mappings or reconciliation writers are added.

Before deployment, stop old writers/consumers, take coordinated database/object/policy/inbox/outbox backups, review the idempotent forward SQL and rehearse on disposable PostgreSQL. Verify slug creation, complete content, current-revision moderation, private claims/bytes, helpful/reports and latest-author rating fallback. Shops/Moderation Down refuses automatic removal of new canonical content; restore coordinated backups and matching binaries under an operator-reviewed rollback. Passing model/SQL/provider checks alone does not establish an actual database upgrade.

New uploads use private `coffeepeek.checkins` (`MinIOOptions__CheckInBucketName`) through `POST /api/Photos/check-in`. Claims bind trusted raster bytes to owner/single parent and immutable reviewed identity. Protected author/public/moderator routes recheck visibility/revision and set private/no-store/nosniff. Internal Media paths remain blocked by the gateway.

Archived locators may still refer to historically public shop-bucket objects. Their retained metadata does not prove source-object anonymous denial. Review historical object/bucket policies and explicit source-object denial on disposable storage before deployment; this remains a release gate. This task performs no object wipe, policy mutation or live backfill and claims no protection of historical source locators.

`dev/unified-checkin-smoke.ps1` defaults to dry-run; `-SelfTest` sends no HTTP. `-Run` requires an isolated loopback stack, `-CoffeeShopSlug` and distinct Author/Other/Moderator tokens. It creates/deletes only its own canonical visits; optional byte/fallback fixtures otherwise report NOT RUN. Existing cooldown/day limits apply.

Disposable PostgreSQL/MinIO/full HTTP, live migrations and deployment are NOT RUN in this source task. Record operator infrastructure checks separately from successful unit/OpenAPI/model/SQL verification.
