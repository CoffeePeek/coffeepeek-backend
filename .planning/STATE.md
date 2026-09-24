---
gsd_state_version: 1.0
milestone: v1.0
milestone_name: milestone
status: unknown
last_updated: "2026-09-22T07:23:02Z"
progress:
  total_phases: 1
  completed_phases: 0
  total_plans: 0
  completed_plans: 0
  percent: 0
---

# State

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
