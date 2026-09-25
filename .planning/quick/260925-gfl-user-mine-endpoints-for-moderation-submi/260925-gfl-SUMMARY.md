---
phase: quick-260925-gfl
plan: 01
status: complete
subsystem: moderation
tags: [moderation, api, contract, user-submissions]
requires: []
provides:
  - GET /api/ModerationShops/mine
  - GET /api/ModerationRoasters/mine
  - GET /api/ModerationReviews/mine
  - userId filter on moderation query repositories
  - ModerationShopDto.RejectedReason, ModerationShopDto.PublishedShopId, ModerationRoasterDto.RejectedReason
affects: [CoffeePeek.Contract, CoffeePeek.ModerationService]
tech-stack:
  added: []
  patterns: ["reuse existing Wolverine message types with an optional trailing record parameter to avoid codegen"]
key-files:
  created:
    - CoffeePeek.ModerationService.Tests/ModerationMineRepositoryFilterTests.cs
    - CoffeePeek.ModerationService.Tests/ModerationMineDtoContractTests.cs
    - CoffeePeek.Moderation.Application.Tests/Features/MyModerationSubmissionsHandlersTests.cs
    - docs/api/moderation-mine.md
  modified:
    - CoffeePeek.Moderation.Domain/Aggregates/*/Repositories/IQueryModeration{Shop,Roaster,Review}Repository.cs
    - CoffeeShop.Moderation.Persistence/Repositories/QueryModeration{Shop,Roaster,Review}Repository.cs
    - CoffeePeek.Moderation.Application/Features/{Shop,Roaster,Review}/GetAllModeration*/ (queries + handlers)
    - CoffeePeek.Contract/Dtos/CoffeeShop/ModerationShopDto.cs
    - CoffeePeek.Contract/Dtos/CoffeeShop/ModerationRoasterDto.cs
    - CoffeePeek.Moderation.Application/Mapper/MapsterConfiguration.ModerationShop.cs
    - CoffeePeek.ModerationService/Controllers/Moderation{Shops,Roasters,Reviews}Controller.cs
decisions:
  - "Moderator GetAll endpoints (shops, roasters, reviews) now also accept ?userId= as a filter-by-submitter option; accepted because moderators already see every item"
  - "/mine actions take page/pageSize/status as separate [FromQuery] params and build the query with UserId from userContext.GetUserIdOrThrow(); no userId input possible"
  - "PublishedShopId maps from ModerationShop.ShopId, null while Guid.Empty"
metrics:
  duration: ~6 min
  completed: 2026-09-25
  tasks: 3
  files: 22
---

# Quick 260925-gfl: User /mine endpoints for moderation submissions Summary

Three authenticated `GET .../mine` endpoints (shops, roasters, reviews) that list only the caller's own submissions. They reuse the existing GetAll queries through an optional `UserId`, so no Wolverine codegen was needed. Shop and roaster DTOs gain additive nullable `rejectedReason` fields, and shops also gain `publishedShopId`.

## Tasks

| Task | Name | Commit |
|------|------|--------|
| 1 | User filter in the three moderation query repositories (+ InMemory tests) | afa4d12d |
| 2 | Forward UserId through GetAll queries/handlers; RejectedReason / PublishedShopId on DTOs (+ handler and mapping tests) | fdf46a8e |
| 3 | GET /mine actions on three controllers + `docs/api/moderation-mine.md` | 5c145d4d |

Task 1 was committed separately with a green build. The GetAll handlers passed the token by name (`cancellationToken: ct` / `ct: ct`) after the signature change. Task 2 then added `query.UserId` forwarding, so its handler tests were properly RED first (compile failure on the missing `UserId`).

## Notes required by the plan

- **Moderator `?userId=`:** `GET /api/ModerationShops`, `GET /api/ModerationRoasters` and `GET /api/ModerationReviews` bind their query record with `[FromQuery]`, so they now also accept `?userId=` as a moderator-only "filter by submitter" option (design decision 2, threat T-gfl-02 accepted).
- **Shop repository InMemory test:** kept. The EF InMemory provider accepted `AsSplitQuery()` in `BuildReviewQuery`, and `Shops_FilteredByUser_ReturnOnlyThatUsersItems` passes.
- No DB migration, no Gateway config change (`git diff --stat -- CoffeePeek.Gateway` is empty), and no new Wolverine message types. `WolverinePreGeneratedHandlersTests` passes.

## Verification

- `dotnet build CoffeePeek.slnx`: succeeded, 0 errors.
- `dotnet test CoffeePeek.ModerationService.Tests`: 46 passed, 0 failed. This includes the 5 new repository filter tests, the 4 new DTO contract tests, WolverinePreGeneratedHandlersTests and OpenApiDocumentGenerationTests.
- `dotnet test CoffeePeek.Moderation.Application.Tests`: 13 passed, 0 failed (includes 5 new handler tests).
- `dotnet test CoffeePeek.Moderation.Domain.Tests`: 191 passed, 0 failed.

## Deviations from Plan

None beyond the optional refactor the plan allowed: each controller gained a private `AddPagingHeaders(...)` helper, and the moderator GetAll action now uses it too. Header behavior is identical.

Doc note: `GET /api/ShopChangeRequests/mine` clamps out-of-range page/pageSize, while the new endpoints return 400 (via `[Range]` + `[ApiController]`). The doc states this difference, and the existing endpoint was left unchanged.

## Known Stubs

None.

## Self-Check: PASSED

- Files: ModerationMineRepositoryFilterTests.cs, ModerationMineDtoContractTests.cs, MyModerationSubmissionsHandlersTests.cs, docs/api/moderation-mine.md all exist.
- Commits afa4d12d, fdf46a8e, 5c145d4d present in `git log`.
