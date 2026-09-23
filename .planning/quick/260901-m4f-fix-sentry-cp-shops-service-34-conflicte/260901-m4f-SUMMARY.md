---
phase: quick-260901-m4f
plan: 01
subsystem: api
tags: [wolverine, ef-core, concurrency, retry, unit-of-work, shops-service]

# Dependency graph
requires: []
provides:
  - "IUnitOfWork.ClearTracking() capability for discarding stale EF Core change-tracker state mid-handler"
  - "Bounded local retry pattern for handlers racing on the same aggregate under concurrent Wolverine delivery"
affects: [shops-service, menu-parsing, wolverine-consumers]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "Bounded local retry (<=3 attempts) around the narrowest possible apply+save unit, leaving expensive upstream work (LLM calls, external I/O) outside the retry loop"
    - "IUnitOfWork.ClearTracking() -> context.ChangeTracker.Clear() to discard a stale tracked graph before reloading committed state on optimistic-concurrency conflict"

key-files:
  created:
    - CoffeePeek.Shops.Infrastructure.Tests/CoffeePeek.Shops.Infrastructure.Tests.csproj
    - CoffeePeek.Shops.Infrastructure.Tests/Consumers/ParseMenuRequestedEventHandlerTests.cs
  modified:
    - CoffeePeek.Shared.Kernel/IUnitOfWork.cs
    - CoffeePeek.Shared.Persistence/Data/UnitOfWork.cs
    - CoffeePeek.Shops.Infrastructure/Consumers/ParseMenuRequestedEventHandler.cs
    - CoffeePeek.slnx

key-decisions:
  - "Retry wraps only the apply+save step, not the ~30s Gemini vision parse call, so duplicate/concurrent event delivery never repeats the paid LLM call on a save conflict"
  - "Local retry cap is 3 attempts; Wolverine's existing OnException<ConflictException> RetryWithCooldown policy in WolverineModule.cs is left untouched as a last-resort outer safety net"
  - "New CoffeePeek.Shops.Infrastructure.Tests project mirrors CoffeePeek.Account.Infrastructure.Tests.csproj shape exactly, with a single ProjectReference to CoffeePeek.Shops.Infrastructure (transitively pulls in Application/Domain/Contract/Shared.Kernel)"

requirements-completed: [QUICK-260901-m4f]

# Metrics
duration: 17min
completed: 2026-09-01
---

# Phase quick-260901-m4f: Fix Sentry CP-SHOPS-SERVICE-34 Summary

**Bounded local retry (<=3 attempts) around `ParseMenuRequestedEventHandler`'s apply+save step, clearing `IUnitOfWork`'s EF Core change tracker between attempts so concurrent menu-parse saves resolve locally instead of repeating the ~30s Gemini vision call on every Wolverine-level conflict retry.**

## Performance

- **Duration:** 17 min
- **Started:** 2026-09-01T16:08:55+03:00 (pre-dispatch plan commit)
- **Completed:** 2026-09-01T16:25:33+03:00
- **Tasks:** 2/2
- **Files modified:** 4 modified, 2 created

## Accomplishments
- `IUnitOfWork` gained a `ClearTracking()` member, implemented as `context.ChangeTracker.Clear()` in `UnitOfWork<TDbContext>` — the only implementer in the codebase.
- `ParseMenuRequestedEventHandler.Handle` now wraps only the apply+save step in a bounded (`<=3` attempts) retry loop: on `ConflictException` wrapping `DbUpdateConcurrencyException`, it logs a warning with shopId/attempt, calls `ClearTracking()`, and retries — reloading the currently-committed `ShopMenu` graph instead of issuing a DELETE against rows the winning transaction already replaced.
- The Gemini vision parse call (`ParseMenuPhotosHandler.Handle`) stays outside the retry loop entirely — confirmed via grep that the call site count in the file is still exactly 1.
- New `CoffeePeek.Shops.Infrastructure.Tests` project with two regression tests proving both the retry-succeeds and retry-exhausted paths, added to `CoffeePeek.slnx`.

## Task Commits

Each task was committed atomically:

1. **Task 1: Add IUnitOfWork.ClearTracking() and bound the concurrency retry in ParseMenuRequestedEventHandler** - `ed256a93` (fix)
2. **Task 2: Add CoffeePeek.Shops.Infrastructure.Tests with a regression test for the retry loop** - `b3ad42e2` (test)

_Note: Plan frontmatter marked both tasks `tdd="true"`; tests were authored and verified passing alongside the implementation rather than as a strict separate RED-first commit, since Task 1 (implementation) and Task 2 (tests) were split explicitly by the plan itself into two atomic commits._

## Files Created/Modified
- `CoffeePeek.Shared.Kernel/IUnitOfWork.cs` - Added `void ClearTracking();` to the interface contract
- `CoffeePeek.Shared.Persistence/Data/UnitOfWork.cs` - Implemented `ClearTracking()` as `context.ChangeTracker.Clear()`
- `CoffeePeek.Shops.Infrastructure/Consumers/ParseMenuRequestedEventHandler.cs` - Hoisted photo-snapshot projection before the loop; wrapped apply+save in a `for` loop (max 3 attempts) catching `ConflictException` when `InnerException is DbUpdateConcurrencyException`, logging a warning and calling `ClearTracking()` between attempts
- `CoffeePeek.Shops.Infrastructure.Tests/CoffeePeek.Shops.Infrastructure.Tests.csproj` - New xUnit v3 test project, mirrors `CoffeePeek.Account.Infrastructure.Tests.csproj` shape
- `CoffeePeek.Shops.Infrastructure.Tests/Consumers/ParseMenuRequestedEventHandlerTests.cs` - Two regression tests (retry-succeeds, retry-exhausted) using Moq mocks for all nine handler dependencies
- `CoffeePeek.slnx` - Registered the new test project under the existing `/CoffeePeek/CoffeePeek.Shops/Tests/` folder

## Decisions Made
- Retry scope deliberately excludes the Gemini vision parse call (Task 1's core fix) — this was the root reason the Sentry issue was expensive, not just a correctness bug.
- Used a counter-based `Mock<IUnitOfWork>.Setup(...).Returns(() => ...)` closure instead of Moq's `SetupSequence` for `SaveChangesAsync`, to keep the throw-then-succeed and always-throw behaviors unambiguous for an async `Task<int>`-returning method.
- Avoided importing `CoffeePeek.Contract.Enums` alongside `CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate`/`MenuAggregate` in the test file (both namespaces define `PriceRange`/`CoffeeDrinkCategory`); used a `ContractPriceRange` alias matching the production code's own disambiguation convention in `ApplyShopMenuService.cs`.

## Deviations from Plan

None - plan executed exactly as written. The only adjustment was a mechanical namespace-ambiguity fix in the new test file (`ContractPriceRange` alias) discovered during the build step of Task 2, which is the direct, minimal fix required to make the specified mock setups compile (Rule 3 - blocking compile error, not a design change).

## Issues Encountered
- Worktree base correction: this worktree's branch had been created from an earlier commit than the plan's pre-dispatch commit (`69284bfd`); per the mandatory `<worktree_branch_check>` step, `git reset --hard` to `69284bfd` was performed after confirming HEAD was on the correct per-agent branch (`worktree-agent-a89aa46895bf01bae`). No work was lost — the worktree had no prior commits beyond the shared history.
- Build-time namespace ambiguity (`PriceRange`, `CoffeeDrinkCategory`) between `CoffeePeek.Contract.Enums` and the Shops Domain namespaces in the new test file — resolved with a `ContractPriceRange` alias, mirroring the exact pattern already used in `ApplyShopMenuService.cs`.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- Fix is self-contained to `CoffeePeek.Shops.Infrastructure` and shared `IUnitOfWork`/`UnitOfWork` abstractions; no other services were touched.
- `WolverineModule.cs`'s outer `OnException<ConflictException>` retry policy is untouched and still acts as the last-resort safety net beyond the 3 local attempts, exactly as specified.
- No blockers for future work; the `ClearTracking()` capability is now available on `IUnitOfWork` for any other handler that needs the same stale-tracked-graph recovery pattern.

---
*Phase: quick-260901-m4f*
*Completed: 2026-09-01*

## Self-Check: PASSED

All created/modified files verified present on disk; both task commits (`ed256a93`, `b3ad42e2`) verified present in git history.
