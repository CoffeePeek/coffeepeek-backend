---
status: complete
---

# Roaster Contribution — Summary

Implements `.planning/specs/ROASTER-CONTRIBUTION.md` end to end. `Roaster` went from a name-only catalog tag to a full profile (geocoded address, Instagram/website contact, photos, about), with a user submission → moderation queue → admin approve/reject → publish pipeline mirroring `ModerationShop`, plus a public detail endpoint.

## What changed

- **Shops domain**: `Roaster` gained `Location`, `RoasterContact`, `About`, and a `RoasterPhoto` collection. New EF migration `AddRoasterProfileFields` (all-nullable, additive).
- **Shops application/API**: Admin CRUD (`AdminRoastersController`) extended to the new fields. New public `GET /api/roasters/{id}` returns the full profile plus the coffee shops carrying that roaster. New `CreateRoasterFromModerationService` + `ModerationRoasterApproveHandler` publish an approved submission into the real catalog, idempotent on moderation id.
- **Moderation domain/persistence**: New `ModerationRoaster` aggregate (mirrors `ModerationShop`'s Create/SetLocation/Approve/Reject shape, scoped down — no schedules/menu/catalog relations). New migration `AddModerationRoaster`.
- **Moderation infrastructure**: `ShopsRoasterExistenceLookup` — a named HttpClient calling Shops' existing public `GET /api/catalogs/roasters` for the submit-time duplicate-name check (mirrors `AccountUserExistenceLookup`'s cross-service pattern in the other direction). No new Shops endpoint was needed for this.
- **Moderation application/API**: `POST /api/ModerationRoasters` (submit, any authenticated user, 409 on duplicate name), `GET` list/by-id and `PUT .../status` (Moderator/Admin) mirror `ModerationShopsController` exactly. Approval publishes `ModerationRoasterApprovedEvent`.
- **Gateway**: new routes for `/api/ModerationRoasters/*` (moderation-submission rate limit) and `/api/roasters/*` (public, shops-cluster).

## Key deviation from the plan

The plan assumed Shops-side geocoding for admin roaster creation; scouting found admin CoffeeShop creation takes lat/long directly from the caller (no geocoding call exists in the Shops service — geocoding is Moderation-only, via Yandex). Admin Create/Update was built the same way: address + explicit lat/long, no geocoding dependency added to Shops.

No `CoffeePeek.Moderation.Application.Tests` project exists in this repo (Moderation's application layer has no unit test coverage anywhere, unlike Shops) — skipped adding one rather than introduce a new test project outside established convention. Domain-level tests (`ModerationRoasterTests.cs`) cover the real business rules (approve/reject gates).

## Verification

- `dotnet build CoffeePeek.slnx` — 0 errors.
- `dotnet test CoffeePeek.slnx` — all 11 test projects pass, 0 failures (Shops.Domain.Tests 134, Moderation.Domain.Tests 178, Shops.Application.Tests 123, Gateway.Tests 31, ModerationService.Tests 5 including Wolverine handler pre-generation and OpenAPI doc generation checks, plus all other unaffected projects).
- Two migrations only: `CoffeePeek.Shops.Persistance/Migrations/20260909123813_AddRoasterProfileFields` and `CoffeeShop.Moderation.Persistence/Migrations/20260909125059_AddModerationRoaster`. Both additive/nullable, no data loss on existing rows.

## Follow-ups not in scope (per spec's Boundaries)

- Browsable roaster directory/search (only detail-by-id exists).
- Resubmission flow for rejected roasters.
- Roaster ownership/claiming.
