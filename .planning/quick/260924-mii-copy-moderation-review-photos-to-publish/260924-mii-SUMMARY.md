---
phase: quick-260924-mii
plan: 01
status: complete
subsystem: shops-reviews
tags: [shops, moderation, reviews, photos, ef-core, backfill]
requires: [quick-260924-jpd]
provides:
  - Review.ReplacePhotos
  - IReviewRepository.TrackPhotoReplacement
  - review photo backfill scripts
affects: [ModerationReviewApprovedHandler, ReviewRepository, ShopsDbContext]
tech-stack:
  added: []
  patterns: ["explicit RemoveRange/AddRange for shared-table child entities with client-side keys"]
key-files:
  created:
    - deploy/scripts/backfill-review-photos-1-export-moderation.sql
    - deploy/scripts/backfill-review-photos-2-import-shops.sql
  modified:
    - CoffeePeek.Shops.Domain/Aggregates/ReviewAggregate/ReviewAction.cs
    - CoffeePeek.Shops.Domain/Aggregates/ReviewAggregate/IReviewRepository.cs
    - CoffeePeek.Shops.Persistance/Repositories/ReviewRepository.cs
    - CoffeePeek.Shops.Persistance/Configuration/ShopsDbContext.cs
    - CoffeePeek.Shops.Infrastructure/Consumers/ModerationReviewApprovedHandler.cs
    - CoffeePeek.Shops.Application.Tests/Features/Review/ModerationReviewApprovedHandlerTests.cs
    - CoffeePeek.Shops.Domain.Tests/Aggregates/ReviewAggregate/ReviewTests.cs
decisions:
  - "Published review photo order = array index of ModerationReviewDto.Photos (moderation sends SortIndex 0 for all)"
  - "Re-approval deletes old ShopPhotos rows explicitly (RemoveRange) and adds new ones explicitly (AddRange); no ShopPhoto key-config change, so no migration"
  - "Backfill is plain SQL in two psql steps via a CSV in the postgres container; no event republish (would Restore() user-deleted reviews)"
metrics:
  duration: ~25min
  completed: 2026-09-24
  tasks: 3
  files: 9
---

# Quick 260924-mii: Copy moderation review photos to published review

ModerationReviewApprovedHandler now maps `reviewDto.Photos` to `ShopPhoto` (SortIndex = array position) and attaches them to the published Shops review on first approval and re-approval, including check-in-originated reviews. It is backed by `Review.ReplacePhotos`, explicit photo row tracking in `ReviewRepository`, and an idempotent two-step psql backfill.

## Commits

| Task | Commit | Description |
|------|--------|-------------|
| 1 | c22e6f44 | Review.ReplacePhotos, TrackPhotoReplacement, Include(Photos), backing-field mapping, handler fix |
| 2 | 93d7dcb3 | 3 handler tests + 1 domain test |
| 3 | 5a0a4ea2 | Backfill scripts (export from cpmoderationdb, import into cpshopsdb) |

## Verification

- `dotnet build CoffeePeek.slnx`: succeeded, 0 errors (513 warnings, all pre-existing categories).
- `dotnet ef migrations has-pending-model-changes` (Shops, dummy connection string): "No changes have been made to the model since the last migration." **No migration is needed.**
- CoffeePeek.Shops.Application.Tests: 177 passed, 0 failed (includes 3 new tests).
- CoffeePeek.Shops.Domain.Tests: 145 passed, 0 failed (includes 1 new test).
- CoffeePeek.Shops.Infrastructure.Tests: 38 passed, 0 failed.
- CoffeePeek.ShopsService.Tests: 8 passed, 0 failed.
- `git diff --stat` on CoffeePeek.Contract: empty.
- Task 3 grep verify: OK. SQL dry-run **not performed**: Docker Desktop was not running, so no local Postgres was available.

## Deviations from Plan

1. **[Rule 1 - Warning hygiene] `#nullable enable` in ModerationReviewApprovedHandlerTests.cs.** The test project has nullable disabled, so the new and existing `?` annotations raised CS8632. I added the same file-level directive used in f915492c.
2. **[Rule 2 - Safety] Extra `i.owner_id IS NOT NULL` guard in the import INSERT.** Shops `OwnerId` is NOT NULL. The export already COALESCEs with the non-null `UserId`, so this guard is only a backstop.
3. **Inserted-count reporting uses `INSERT ... RETURNING` inside a CTE instead of GET DIAGNOSTICS.** GET DIAGNOSTICS only works in plpgsql. The script also reports the total count of reviews with photos.

## Notes

- The temp import table uses `ON COMMIT DROP`. For a dry-run, replace `COMMIT` with `ROLLBACK`.
- The SQL files are stored with LF endings in the index. With `core.autocrlf=true`, a Windows working copy gets CRLF. On the Linux VPS checkout they will be LF.
- The Wolverine `Handle` signature is unchanged, so the generated code needs no regeneration.

## Known Stubs

None.

## Threat Flags

None. T-mii-02, T-mii-03 and T-mii-04 are mitigated as planned: NOT EXISTS idempotency plus a single transaction with ON_ERROR_STOP, no updates to "Reviews", and length-limit report/skip.

## Self-Check: PASSED

- All 9 files in key-files exist.
- Commits c22e6f44, 93d7dcb3, 5a0a4ea2 exist on dev.
