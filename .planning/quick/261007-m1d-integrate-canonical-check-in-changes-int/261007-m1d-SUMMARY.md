---
phase: quick-261007-m1d
plan: "01"
status: complete
subsystem: check-ins
requires: [current-main-0dc0940c, source-net-delta-641e1bd0-to-44bd58b0]
provides: [canonical-v1-with-main-slugs, archive-preserving-forward-schema, verified-main-descendant-integration]
affects: [shops, moderation, media, account, gateway]
tags: [dotnet10, efcore, wolverine, privacy, public-slugs]
tech-stack:
  added: []
  patterns: [existing-public-address-projection, frozen-consumed-drinks, transactional-outbox, additive-archive-schema]
key-decisions:
  - Preserve current main public slugs and optional drinkSlug/customDrinkName including Other.
  - Keep all legacy rows/tables as physical archives outside the supported runtime model.
  - Retarget existing reports without dropping complaint/audit history or adding a reporting subsystem.
  - Root alone fetches/revalidates and performs the authorized normal main push.
metrics:
  duration: "58 minutes since scoped patch generation"
  completed: "2026-10-07"
---

# Quick 261007-m1d: canonical check-ins integrated into current main

Canonical v1 check-ins now reuse main's public slug/address and consumed-drink flows, with fresh complete-content tables and preserved legacy archives.

## Completion and commits

All 3 tasks complete. Execution worktree: `C:/Users/User/.codex/worktrees/check-ins-main-integration/CoffeePeek-NET`; branch `codex/check-ins-main`. Baseline `0dc0940cf1fad19fd4915f475d5aa8c2304ca131` remains an ancestor. Scope was ONLY net diff `641e1bd0..44bd58b0`, adapted to newer main; commit641 itself and primary dirty content were not imported.

1. `104ec2a2c69ea4ab7634caa44c1954c4ed4c0923` — integrate canonical source, main slug/drink projections, privacy/revision/ratings, existing reports and relevant regressions.
2. `3d9419fd36f4cf11b9ceb399a4fe10ed9e7da43e` — additive archived schema, four regenerated Wolverine hosts, complete MVC/projector examples and release/smoke guidance.
3. This PLAN/SUMMARY metadata commit records completed verification; its SHA is in the executor's final handoff.

Primary checkout source/STATE/ROADMAP/history were not edited. Only this current PLAN/SUMMARY is copied back for the root. No remote push, PR, deployment, live database migration or storage deletion was performed by the executor.

## Result and preservation

Public POST accepts `coffeeShopSlug`, mandatory text and three 1–5 scores, optional `drinkSlug`/`customDrinkName`, photos/time and Public/Private visibility. Existing address registries resolve internal IDs; public related identities remain slug addresses. Operational visit/photo IDs remain. Public projection includes frozen Russian/English/custom drink names and no new shop/user/drink/city UUIDs.

Canonical eligibility, latest approved author/shop rating fallback, all eligible visits, helpful votes, immutable revision moderation, replay/sequence checks, trusted single-parent photo claims/MIME/bytes and current-access protected reads are retained. Main coffee import/catalog/discovery/admin, public registries/profile guards, patched package/OpenAPI behavior, storage Put/health/unique keys and durable post-commit creation cache invalidation remain.

Existing reporting is preserved at `/api/v1/check-ins/{id}/reports` and `/api/admin/check-in-reports`, with authentication/submission limiter/admin policy, canonical visibility checks and complaint/admin audit. Internal ReviewReport naming/storage column remains where needed. Unavailable archival targets return no current content.

Historical main migration/designer files are unchanged; only new migrations and current snapshots differ. No package/tool/library versions or main CI project selection were changed. Imported modified C# bundles were split under the one-top-level-type convention. Changed-source type scan and conflict/whitespace checks pass. Retired review source/tests/generated adapters and obsolete bundles are intentional deletions; unrelated main functionality was preserved.

## Forward migrations and offline evidence

| Context | Main head | New migration |
| --- | --- | --- |
| Shops | `20261007065353_AddDominicanCoffeeOrigin` | `CoffeePeek.Shops.Persistance/Migrations/20261007133435_CanonicalCheckInsOnMain.cs` |
| Moderation | `20261005081410_RemoveLegacyImportSource` | `CoffeeShop.Moderation.Persistence/Migrations/20261007133453_CanonicalCheckInSnapshotsOnMain.cs` |
| Account | `20261005080557_MoveUserSlugToUserTable` | `CoffeePeek.Account.Persistence/Migrations/20261007132511_RetireReviewCountOnMain.cs` |
| Media | `20260621070255_Update2026-06-20-1` | `CoffeePeek.MediaService/Migrations/20261007132516_CanonicalCheckInPhotoClaimsOnMain.cs` |

Each has its generated Designer and updated context snapshot. Main historical chains/designer content were checked against the baseline. All four `has-pending-model-changes` and idempotent SQL commands exit0 using a design-only dummy connection. Idempotent SQL resides in `.tmp/shops-main-upgrade.sql`, `.tmp/moderation-main-upgrade.sql`, `.tmp/account-main-upgrade.sql`, `.tmp/media-main-upgrade.sql`. Model/script logs/results are `.tmp/ef-{shops,moderation,account,media}-model-check.log`, corresponding `-script.log` and `-results.json`.

New physical runtime tables are `CanonicalCheckIns` / `ModerationCheckIns`; new shadow photo FKs are `CanonicalCheckInId` / `ModerationCheckInId`, distinct from archival columns. The forward SQL has no legacy DELETE, DROP TABLE/COLUMN, RENAME or archival data copy. Legacy CheckIns/Reviews/ReviewHelpfulVotes/ModerationReviews/old-photo columns and all rows remain outside runtime. The existing report FK alone is detached to permit canonical targets; report rows/raw old targets remain. The ignored Account old counter column remains with default0 to satisfy old NOT NULL for new inserts. New runtime visits start empty; no historical object is inferred public or claimed/protected automatically.

Offline additive schema regression verifies actual generated idempotent SQL, physical canonical table mapping and separate photo association. Four Wolverine `codegen write` runs succeeded; logs `.tmp/codegen-{shops,moderation,account,media}.log`. All unrelated main handlers were regenerated and retained; obsolete review adapters removed.

## Verification

- Final generated backend build: **0 errors**, 324 existing/analyzer warnings, `.tmp/integration-build-generated.log`; preceding final model/source build `.tmp/integration-build-final.log` also0errors. No unrelated warning cleanup performed.
- Current main CI covers11 test suites. Initial full run passed1577, failed2 outdated Gateway review-route assertions and skipped31 live-Postgres tests (`.tmp/integration-full-ci.log`). Both assertions were updated for the authorized canonical retirement and the entire Gateway suite passed **68/68** (`.tmp/integration-gateway-final.log`). All other full suites already passed. Final current-CI coverage: **1579 passed,31 skipped,0 unresolved failures**.
- Full missing ModerationService suite: **62/62 passed**, `.tmp/integration-moderation-service-full.log`. Combined distinct full-suite coverage: **1641 passed,31 skipped**. Focused helper/identity/privacy/OpenAPI tests overlap those totals and are not counted again.
- Actual Shops OpenAPI generation and canonical POST/GET/mine/request required-slug/public-response schemas pass; old review routes absent. Full ShopsService72 and Media57 pass in CI. Main account gateway/profile/public-catalog smoke remains covered.
- Four full JSON fixtures were generated by actual configured MVC serialization plus existing PublicDiscoveryService projection;14 seed catalog rows remain slug-only. Temporary write hook/generator was removed. `ApiExamplesContractTests` passes, including complete null/envelope/named-enum shape and no aggregate UUID leakage.
- Smoke `-SelfTest` **PASS**, sends no HTTP: `.tmp/smoke-selftest.log`.
- Current pinned package vulnerability audit: **no vulnerable packages reported**, `.tmp/integration-vulnerability-audit.log`.
- Main ancestry, no unmerged markers/index entries, changed-type layout, preserved historical chains and `git diff --check` pass. No known stubs prevent the source objective.

## Deviations and automatic approval reviews

**Main adaptation (correctness):** primary/source premise of absent slug resolvers was false on current main. Existing registry/PublicDiscoveryService and DrinkSelection/IDrinkCatalog were reused; obsolete UUID examples/requests were replaced. Current main discovery ratings/counts and report/admin Gateway tests were retargeted, preserving their other behavior. No package upgrades or additional subsystem.

**TDD gate deviation:** this was a net-delta integration of already implemented/tested source into divergent main. Production adaptation preceded runnable integration tests; no separate new RED gate commit is claimed. Existing canonical tests were adapted and focused/full checks executed; the additive schema regression is covered in final full CI.

**Automatic approval rejection1 (no action executed):** a proposed broad mutation script deleting ReviewAggregate/ReviewReport domain/repositories/config/controller was rejected because unresolved main-specific behavior required retargeting/preservation and reporting loss exceeded authorized scope. Safe replacement retained the existing report feature/table/audit and retargeted current canonical content. The rejected script was not executed.

**Automatic approval rejection2 (no action executed):** a proposed migration script with irreversible DELETE of legacy CheckIns, ShopPhotos and moderation rows and non-reconstructible Down was rejected because user authorization did not clearly cover that broad data-deletion scope/all affected records. Safe replacement uses fresh canonical physical tables and separate photo links, preserving all old rows/tables/columns; no rejected DELETE script ran. The root accepted this safer approach and will disclose the review rejection to the user; no additional permission flow was needed for unaffected/safe reversible preparation.

## NOT RUN / release limits

- **31 existing live-Postgres tests skipped** because their dedicated live connection/environment was unavailable (Account9, Shops22); exact names are in the full CI log.
- Actual PostgreSQL upgrade/idempotent execution/rollback and metadata/archival retention rehearsal NOT RUN. Generated SQL/model/provider tests are offline evidence only.
- Actual MinIO private bucket/source-object policy and historical anonymous-byte denial checks NOT RUN. Archived old locators may still refer to historically public shop-bucket objects; archive preservation proves no privacy policy change. Historical source-object/policy review remains a release gate before deployment, documented in `docs/migrations/unified-check-ins.md`.
- Full disposable Gateway+Postgres+RabbitMQ+Redis+MinIO HTTP smoke NOT RUN; optional actual photo/rating-fallback smoke fixtures NOT RUN. Unit/loopback mocked-host HTTP coverage is distinguished from a full stack.
- No Docker repair, live migration, data/object wipe, deployment or executor push. Docker Desktop was previously unavailable in this session. Root alone performs the separately authorized fetch/revalidation/normal main push after this handoff.

## Self-Check: PASSED

Both implementation commits exist, branch descends from0dc0940c, all8new migration/designer files/four snapshots/generated registries/four examples/logs/PLAN/SUMMARY exist, all4EFresult files record model/sql exit0, and current source is conflict-free. Source/test checks are complete; remote/infrastructure actions are intentionally outside executor scope.
