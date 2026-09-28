---
gsd_state_version: 1.0
milestone: v1.0
milestone_name: milestone
status: in_progress
last_updated: "2026-09-28T07:45:00Z"
progress:
  total_phases: 6
  completed_phases: 0
  total_plans: 2
  completed_plans: 2
  percent: 0
---

# State

## Active work: public slugs (2026-09-27)

- Approved full staged rollout: `specs/PUBLIC-SLUGS.md`.
- Phase 1 implemented: commits `7aba62c2`, `0e562a2a`; Shops domain 173/173 and infrastructure 49/49 pass.
- Infrastructure verification debt: live PostgreSQL uniqueness/FK/concurrency tests pending; Docker startup requires sudo password. Model/SQL verification is not live DB verification.
- Phase 2 implementation complete: coffee shop/roaster writer coordination, slug APIs, explicit rename, bounded backfill, audit migration and compatibility tests.
- Phase 2 verification: 461 affected tests and the CI solution build pass. Live PostgreSQL uniqueness/FK/advisory-lock/savepoint checks remain required, so `PublicAddresses__Enabled` stays false by default.
- Frontend is outside this repository; page redirects remain a later external integration step.

## Quick Tasks Completed

| ID | Slug | Status | Date |
|----|------|--------|------|
| 260801-erp | remove-community-social-layer-feed-posts | complete | 2026-08-01 |
| 260815-fast | widen-import-candidate-phone | complete | 2026-08-15 |
| 260815-fast | admin-overview-always-returns | complete | 2026-08-15 |
| 260831-krk | add-full-crud-create-update-delete-for-c | complete | 2026-08-31 |
| 260901-e0n | fix-400-validation-failed-on-post-api-mo | complete | 2026-09-01 |
| 260901-el2 | fix-checkins-createdat-returning-default | complete | 2026-09-01 |
| 260901-faa | fix-checkin-photos-on-create-persist-pri | complete | 2026-09-01 |
| 260901-m4f | fix-sentry-cp-shops-service-34-conflicte | complete | 2026-09-01 |
| 260909 | roaster-contribution | complete | 2026-09-09 |
| 260914 | coffee-shop-data-completeness | complete | 2026-09-14 |
| 260914-zones | coffee-zones-and-map-clustering | complete | 2026-09-14 |
| 260917-f9m | link-published-shops-reviews-to-source-m | complete | 2026-09-17 |
| 260921 | account-deletion-workflow-slice | complete | 2026-09-21 |
| 260922 | shop-change-requests | complete | 2026-09-22 |
| 260924-hek | account-security-group1-fixes | complete | 2026-09-24 |
| 260924-jpd | harden-review-and-check-in-photo-attachm | complete | 2026-09-24 |
| 260924-mii | copy-moderation-review-photos-to-publish | complete | 2026-09-24 |
| 260924-oxa | load-moderation-review-photos-on-approve | complete | 2026-09-24 |
| 260925-ei9 | admin-stats-expansion-dau-wau-mau-timese | complete | 2026-09-25 |
| 260925-kjj | add-optional-date-range-filter-from-to-t | complete | 2026-09-25 |
| 260925-l0p | imgproxy-image-variants-with-urls-on-pho | complete | 2026-09-25 |

## Milestone Progress

**Tier 0** waves **A–E** are done (security, account/auth, admin sessions, password reset, check-ins/favorites cleanup, related tests).

## Specs / next

- Shop filters & tags SPEC written at `.planning/specs/SHOP-FILTERS-TAGS.md`.
- Catalog ingest + admin queue SPEC: `.planning/specs/SHOP-IMPORT-ADMIN.md` (backend vs frontend handoff).
- Coffee shop menu (catalog drinks + Gemini photo parse): `.planning/specs/SHOP-MENU.md`.
- Roaster contribution (user submission -> moderation -> publish, public detail endpoint) SPEC + implementation: `.planning/specs/ROASTER-CONTRIBUTION.md`, `.planning/quick/260909-roaster-contribution/`.

## Accumulated Context

### Exploration (2026-08-15)

Specialty coffee shop bootstrap for Minsk: OSM + lists → admin review queue → catalog. See note `2026-08-15-specialty-coffee-shops-minsk-import`, seed `SEED-001`.

Spike 001 VALIDATED: Overpass Minsk = 1576 candidates (priority ~380, vending 96). OSM is not a specialty catalog. Draft aggregate: `ShopImportCandidate`, not `ModerationShop`. Import labels **ShopKind** (Specialty / GoodCoffee / Cafe / ToGo); reject stays out of the feed. Review card must show Instagram + Yandex photos/images + website preview — coordinates alone are not enough. OSM objects not edited in 5+ years are dropped from the queue.

### Account deletion (2026-09-21)

First slice complete: pending request persistence, create/resend/current/cancel APIs, HMAC-protected confirmation tokens, and immediate confirmation email. Users and sessions remain active; confirmation and cross-service cleanup are deferred.

### Pending Todos

_None._
