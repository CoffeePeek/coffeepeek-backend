---
phase: quick-260925-ei9
plan: 01
status: complete
subsystem: admin-stats
tags: [admin, stats, account, shops, moderation, gateway, docs]
requires: []
provides:
  - GET /api/admin/stats/overview (extended with DAU/WAU/MAU, deleted, email-confirmed, Google)
  - GET /api/admin/stats/users/timeseries
  - GET /api/admin/stats/shops/timeseries
  - GET /api/admin/stats/shops/insights
  - GET /api/admin/stats/moderation/insights
  - docs/api/admin-stats.md
affects: [CoffeePeek.Contract, CoffeePeek.Gateway, AccountService, ShopsService, ModerationService]
tech-stack:
  added: []
  patterns: [zero-filled-daily-series, sequential-dbcontext-queries, static-typeload-codegen]
key-files:
  created:
    - CoffeePeek.Contract/Dtos/Admin/AdminTimeseriesDtos.cs
    - CoffeePeek.Contract/Dtos/Admin/AdminShopsInsightsDto.cs
    - CoffeePeek.Contract/Dtos/Admin/AdminModerationInsightsDto.cs
    - CoffeePeek.Shared.Kernel/Extentions/DailySeries.cs
    - CoffeePeek.Account.Application/Features/Admin/Stats/GetAdminUsersTimeseriesHandler.cs
    - CoffeePeek.Shops.Application/Features/Admin/Stats/GetAdminShopsInsightsHandler.cs
    - CoffeePeek.Shops.Application/Features/Admin/Stats/GetAdminShopsTimeseriesHandler.cs
    - CoffeePeek.Moderation.Application/Features/Admin/Stats/GetAdminModerationInsightsHandler.cs
    - CoffeePeek.Account.Infrastructure.Tests/Persistence/AdminUserQueryRepositoryTests.cs
    - CoffeePeek.Shops.Infrastructure.Tests/Persistence/AdminStatsQueryRepositoryTests.cs
    - CoffeePeek.ModerationService.Tests/AdminModerationStatsQueryRepositoryTests.cs
    - docs/api/admin-stats.md
  modified:
    - CoffeePeek.Contract/Dtos/Admin/AdminOverviewStatsDto.cs
    - CoffeePeek.Account.Persistence/Repositories/AdminUserQueryRepository.cs
    - CoffeePeek.Shops.Persistance/Repositories/AdminStatsQueryRepository.cs
    - CoffeeShop.Moderation.Persistence/Repositories/AdminModerationStatsQueryRepository.cs
    - CoffeePeek.Gateway/appsettings.json
decisions:
  - "DAU/WAU/MAU = distinct RefreshToken.UserId created in rolling 24h/7d/30d"
  - "BlockedUsers excludes soft-deleted; ActiveUsers = !blocked && !deleted (documented behavior change)"
  - "New AdminUserStats params appended with defaults (non-breaking for existing positional callers)"
  - "Shops/Moderation repositories return domain records (Domain projects do not reference Contract); handlers map + round"
  - "Moderation reviews queue uses the same pending definition as the overview's pendingModerationReviews (soft-deleted not excluded)"
metrics:
  completed: 2026-09-25
  tasks: 3
---

# Quick 260925-ei9: Admin stats expansion Summary

Admin dashboard stats now include real engagement (DAU/WAU/MAU from refresh-token creation), zero-filled per-UTC-day timeseries for users/shops/reviews/check-ins, rating distribution + top shops + app download stats, and moderation SLA/queue age/moderator workload. All four new endpoints go through the gateway under the Admin policy, and `docs/api/admin-stats.md` is the frontend contract.

## Commits

| Task | Commit | Description |
|------|--------|-------------|
| 1 | 967970b4 | Contract DTOs, DailySeries, Account DAU/WAU/MAU + users timeseries, codegen |
| 2 | c6de92c5 | Shops insights + timeseries, persistence/handler tests, codegen |
| 3 | a7263752 | Moderation insights, gateway routes + tests, codegen, API doc |

## Verification

- `dotnet build CoffeePeek.slnx`: succeeded (only pre-existing CS0618 warnings)
- `dotnet test CoffeePeek.slnx`: 12 projects, 1183 passed, 0 failed, 0 skipped
- All three WolverinePreGeneratedHandlersTests pass (Account, Shops, Moderation)
- No `Migrations/` changes; `Directory.Packages.props` unchanged

## Deviations from Plan

1. **Codegen reorder noise reverted.** `codegen write` also rewrote `ChangePasswordCommandHandler`, `GetAdminUsersQueryHandler` (Account) and `ParseMenuPhotosCommandHandler` (Shops), but only moved one statement. I reverted those files to keep the commits focused. The pre-generated handler tests still pass.
2. **`AdminUserStats` new params have defaults.** They are appended as `= 0` params, so existing positional callers keep compiling. The shape matches the plan otherwise.
3. **Extra helper `DailySeries.WindowStartUtc`.** Added so Account and Shops handlers compute the timeseries window the same way.
4. **Ambiguous enum names in the moderation test.** `ShopChangeSection` and `ShopIssueCategory` exist in both Contract and Moderation.Domain. I removed the Contract using and use the domain enums.

`deploy/docker-compose.yml` only overrides cluster addresses, not routes, so nothing needed mirroring there.

## Known Stubs

None.

## Threat Flags

None. The new surface matches the threat model: Admin policy is on both the gateway routes and the service controllers, `days` is clamped to 1..90, and the in-memory SLA load covers only 30 days.

## Self-Check: PASSED

- Commits 967970b4, c6de92c5, a7263752 are in `git log`
- docs/api/admin-stats.md and all new source/test files exist
