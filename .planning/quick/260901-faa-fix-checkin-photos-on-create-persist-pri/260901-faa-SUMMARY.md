---
status: complete
---

# Quick Task 260901-faa: Fix check-in persistence + expose photos on create

**Plan:** [260901-faa-PLAN.md](./260901-faa-PLAN.md)
**Tasks:** 3/3 complete
**Duration:** ~20 min (executor hit a transient API error mid-verification after all 3 commits landed; orchestrator finished verification and merge)

## What prompted this

User asked to add photo support on check-in creation. Investigation showed the write-side plumbing
(`CreateCheckInCommand.Photos`, `CheckIn.AddPhotos`, EF's auto-discovered `CheckInId` shadow FK on
`ShopPhotos`) already existed, but surfaced two real bugs along the way.

## Bug 1: private check-ins were never persisted

`CreateCheckInHandler.Handle` only called `queryCheckInRepository.Add(checkIn)` inside the
`if (command.IsPublic)` branch. Private check-ins (`IsPublic == false`) were built in memory,
had note/photos attached, but were never added to the EF `DbSet` — `SaveChangesAsync()` had nothing
to persist. The existing test only asserted `SaveChangesAsync` was *called*, not that anything was
actually saved, so it didn't catch this.

Root cause of why `Add` was gated on `IsPublic` in the first place: `CheckIn.Rating` is a
non-nullable, EF-required owned navigation, only ever set via `AssignRating(...)` — which itself
only ran inside the `IsPublic` branch. Simply moving `Add()` out would have made every private
check-in throw at `SaveChangesAsync()` instead of silently no-op'ing.

**Fix:**
- `CheckIn`'s private constructor now defaults `Rating = new Rating(0, 0, 0)` (using `Rating`'s
  existing `internal` unvalidated constructor), so the owned navigation is never null.
- `queryCheckInRepository.Add(checkIn)` now runs unconditionally, before the `IsPublic`-only rating
  assignment / review-publish block.

## Bug 2 / feature gap: photos invisible on read

`CheckInDto` (`GET /api/CheckIns`) had no `Photos` field, so a client could never see photos it had
just uploaded, even though they were being saved once Bug 1 is fixed.

**Fix:** Added `ShortPhotoMetadataDto[] Photos` to `CheckInDto`, mapped from `CheckIn.ShopPhotos` in
`MapsterConfiguration.cs`, mirroring the exact existing `CoffeeShop.ShopPhotos -> ShortShopDto.Photos`
pattern (same shared `ShopPhoto -> ShortPhotoMetadataDto` config, including computed `FullUrl`).

## Files touched

- `CoffeePeek.Shops.Domain/Aggregates/CheckInAggregate/CheckIn.cs` — default zero-value `Rating`
- `CoffeePeek.Shops.Application/Features/CheckIn/CreateCheckIn/CreateCheckInHandler.cs` — unconditional `Add`
- `CoffeePeek.Contract/Dtos/CoffeeShop/CheckInDto.cs` — new `Photos` property
- `CoffeePeek.Shops.Application/Mapper/MapsterConfiguration.cs` — `Photos` mapping
- `CoffeePeek.Shops.Domain.Tests/Aggregates/CheckInAggregate/CheckInTests.cs` — new default-Rating test
- `CoffeePeek.Shops.Application.Tests/Features/CheckIn/CreateCheckIn/CreateCheckInHandlerTests.cs` — Add-verification + photos test
- `CoffeePeek.Shops.Application.Tests/Mapper/MapsterConfigurationCheckInTests.cs` — Photos/FullUrl test

## Commits

- `a4fadd33`: fix(quick-260901-faa): default CheckIn.Rating to zero-value to satisfy EF owned navigation
- `00e81e53`: fix(quick-260901-faa): persist private check-ins by unconditionally calling repository.Add
- `02511fda`: feat(quick-260901-faa): expose check-in photos in CheckInDto via Mapster
- `b33f6bb8`: chore: merge quick task 260901-faa check-in persistence + photos fix (worktree-agent-a0e2fd76a0f11efc9)

## Verification

- `dotnet build CoffeePeek.slnx` — 0 errors (re-verified on merged main).
- `dotnet test CoffeePeek.Shops.Domain.Tests --filter FullyQualifiedName~CheckInTests` — 7/7 pass.
- `dotnet test CoffeePeek.Shops.Application.Tests --filter "CreateCheckInHandlerTests|MapsterConfigurationCheckInTests"` — 7/7 pass.
- Full `CoffeePeek.Shops.Application.Tests` — 92/92 pass.
- Full `CoffeePeek.Shops.Domain.Tests` — 117/117 pass.
- No regressions; production diffs reviewed against plan before merge.

No deviations from plan. The executor died mid-way through its own final verification step due to a
transient API error (not a task failure) — all 3 commits were already in place and correct; the
orchestrator completed the verification, merge, and cleanup.
