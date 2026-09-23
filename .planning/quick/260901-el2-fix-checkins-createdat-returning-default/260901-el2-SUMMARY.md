---
status: complete
---

# Quick Task 260901-el2: Fix CheckIns createdAt returning default DateTime

**Plan:** [260901-el2-PLAN.md](./260901-el2-PLAN.md)
**Tasks:** 1/1 complete
**Duration:** ~15 min

## Root cause

`GET /api/CheckIns` returned `createdAt: "0001-01-01T00:00:00"` for every check-in. `CheckInDto.CreatedAt`
has no same-named counterpart on the `CheckIn` domain entity — the entity only exposes `CreatedAtUtc`
(inherited from `Entity<TId>`). Mapster only matches properties by exact name, and the
`config.NewConfig<CheckIn, CheckInDto>()` block in `MapsterConfiguration.cs` only did
`.Ignore(dest => dest.ShopName)` — it never mapped `CreatedAt`, so `ProjectToType<CheckInDto>`
(used by `CheckInQueries.GetByUserId`) left it at `default(DateTime)`.

## Fix

- Added `.Map(dest => dest.CreatedAt, src => src.CreatedAtUtc)` to the `CheckIn -> CheckInDto`
  Mapster config in `CoffeePeek.Shops.Application/Mapper/MapsterConfiguration.cs`.
- Added a regression test (`CoffeePeek.Shops.Application.Tests/Mapper/MapsterConfigurationCheckInTests.cs`)
  that builds the real production `MapsterConfiguration.CreateConfig(...)`, creates a `CheckIn` via
  `CheckIn.Create(...)`, adapts it to `CheckInDto`, and asserts `CreatedAt` equals the entity's
  `CreatedAtUtc` (and is not `default(DateTime)`).
- `CheckInDto.cs` (shared contract) and `CheckInQueries.cs` are untouched.

## Commits

- `17666b30`: fix(quick-260901-el2): map CheckInDto.CreatedAt from CheckIn.CreatedAtUtc
- `<merge>`: chore: merge quick task 260901-el2 CheckIns createdAt fix (worktree-agent-a8491db0d40ae3c47)

## Files touched

- `CoffeePeek.Shops.Application/Mapper/MapsterConfiguration.cs`
- `CoffeePeek.Shops.Application.Tests/Mapper/MapsterConfigurationCheckInTests.cs` (new — 1 test)

## Verification

- `dotnet build CoffeePeek.slnx` — 0 errors.
- `dotnet test CoffeePeek.Shops.Application.Tests --filter FullyQualifiedName~MapsterConfigurationCheckInTests` — 1/1 pass.
- Full `CoffeePeek.Shops.Application.Tests` suite — 90/90 pass (no regressions), re-verified after merge to main.

No deviations from plan.
