---
phase: quick-260925-gfl
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - CoffeePeek.Moderation.Domain/Aggregates/ModerationShopAggregate/Repositories/IQueryModerationShopRepository.cs
  - CoffeePeek.Moderation.Domain/Aggregates/ModerationRoasterAggregate/Repositories/IQueryModerationRoasterRepository.cs
  - CoffeePeek.Moderation.Domain/Aggregates/ModerationReviewAggregate/Repositories/IQueryModerationReviewRepository.cs
  - CoffeeShop.Moderation.Persistence/Repositories/QueryModerationShopRepository.cs
  - CoffeeShop.Moderation.Persistence/Repositories/QueryModerationRoasterRepository.cs
  - CoffeeShop.Moderation.Persistence/Repositories/QueryModerationReviewRepository.cs
  - CoffeePeek.ModerationService.Tests/ModerationMineRepositoryFilterTests.cs
  - CoffeePeek.Contract/Dtos/CoffeeShop/ModerationShopDto.cs
  - CoffeePeek.Contract/Dtos/CoffeeShop/ModerationRoasterDto.cs
  - CoffeePeek.Moderation.Application/Mapper/MapsterConfiguration.ModerationShop.cs
  - CoffeePeek.Moderation.Application/Features/Shop/GetAllModerationShops/GetAllModerationShopsQuery.cs
  - CoffeePeek.Moderation.Application/Features/Shop/GetAllModerationShops/GetAllModerationShopsHandler.cs
  - CoffeePeek.Moderation.Application/Features/Roaster/GetAllModerationRoasters/GetAllModerationRoastersQuery.cs
  - CoffeePeek.Moderation.Application/Features/Roaster/GetAllModerationRoasters/GetAllModerationRoastersHandler.cs
  - CoffeePeek.Moderation.Application/Features/Review/GetAllModerationReviews/GetAllModerationReviewsQuery.cs
  - CoffeePeek.Moderation.Application/Features/Review/GetAllModerationReviews/GetAllModerationReviewsHandler.cs
  - CoffeePeek.Moderation.Application.Tests/Features/MyModerationSubmissionsHandlersTests.cs
  - CoffeePeek.ModerationService.Tests/ModerationMineDtoContractTests.cs
  - CoffeePeek.ModerationService/Controllers/ModerationShopsController.cs
  - CoffeePeek.ModerationService/Controllers/ModerationRoastersController.cs
  - CoffeePeek.ModerationService/Controllers/ModerationReviewsController.cs
  - docs/api/moderation-mine.md
autonomous: true
requirements: [QUICK-260925-gfl]

must_haves:
  truths:
    - "An authenticated non-moderator calling GET /api/ModerationShops/mine gets only moderation shops whose UserId equals their own id, paged, newest first"
    - "An authenticated non-moderator calling GET /api/ModerationRoasters/mine gets only their own roaster submissions"
    - "An authenticated non-moderator calling GET /api/ModerationReviews/mine gets only their own review submissions"
    - "Each /mine endpoint accepts optional page, pageSize, status query params and returns X-Total-Count, X-Total-Pages, X-Current-Page, X-Page-Size headers"
    - "The user id filter comes only from IUserContext.GetUserIdOrThrow(); no query string value can make /mine return another user's items"
    - "Unauthenticated calls to any /mine endpoint return 401"
    - "Each shop item exposes moderationStatus, rejectedReason and publishedShopId (null until the shop is published); each roaster item exposes moderationStatus and rejectedReason; each review item exposes moderationStatus and rejectedReason (already present)"
    - "GET /api/ShopChangeRequests/mine keeps working unchanged and is documented alongside the new endpoints"
  artifacts:
    - path: "CoffeeShop.Moderation.Persistence/Repositories/QueryModerationShopRepository.cs"
      provides: "userId filter on GetPagedForReviewAsync"
      contains: "userId"
    - path: "CoffeePeek.ModerationService/Controllers/ModerationShopsController.cs"
      provides: "GET mine action"
      contains: "HttpGet(\"mine\")"
    - path: "CoffeePeek.ModerationService/Controllers/ModerationRoastersController.cs"
      provides: "GET mine action"
      contains: "HttpGet(\"mine\")"
    - path: "CoffeePeek.ModerationService/Controllers/ModerationReviewsController.cs"
      provides: "GET mine action"
      contains: "HttpGet(\"mine\")"
    - path: "CoffeePeek.Moderation.Application.Tests/Features/MyModerationSubmissionsHandlersTests.cs"
      provides: "handler tests proving UserId is forwarded to repositories"
    - path: "CoffeePeek.ModerationService.Tests/ModerationMineRepositoryFilterTests.cs"
      provides: "InMemory repository tests proving the user filter"
    - path: "docs/api/moderation-mine.md"
      provides: "client doc for all four /mine endpoints"
  key_links:
    - from: "ModerationShopsController.GetMine"
      to: "GetAllModerationShopsQuery"
      via: "new query built with UserId: userContext.GetUserIdOrThrow()"
      pattern: "GetUserIdOrThrow"
    - from: "GetAllModeration*Handler"
      to: "IQueryModeration*Repository paged method"
      via: "query.UserId passed as userId argument"
      pattern: "query\\.UserId"
---

<objective>
Let an authenticated client list the moderation items they submitted (coffee shops, roasters, reviews) with their moderation status, rejection reason, and (for shops) the published shop id. Shop change requests already have `GET /api/ShopChangeRequests/mine`; only document it.

Purpose: users currently cannot see what happened to their submissions.
Output: three `GET .../mine` endpoints, a user filter in the three query repositories, two additive DTO fields per shop and one per roaster, tests, and a client API doc.
</objective>

<execution_context>
@$HOME/.claude/get-shit-done/workflows/execute-plan.md
@$HOME/.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@.planning/STATE.md
@./CLAUDE.md
@CoffeePeek.ModerationService/Controllers/ShopChangeRequestsController.cs
@CoffeePeek.ModerationService/Controllers/ModerationShopsController.cs
@CoffeePeek.ModerationService/Controllers/ModerationRoastersController.cs
@CoffeePeek.ModerationService/Controllers/ModerationReviewsController.cs
@CoffeePeek.Moderation.Application.Tests/Features/ModerationStatusHandlersTests.cs
@CoffeePeek.ModerationService.Tests/ModerationReviewRepositoryTests.cs
@CoffeePeek.ModerationService.Tests/ModerationRoasterDtoContractTests.cs
@docs/api/photos-client.md

<design_decisions>
Why these choices were made (the executor must follow them):

1. Reuse the existing GetAll queries/handlers. Do not add new query types. Append a trailing optional `Guid? UserId = null` parameter to each GetAll query record.
   Reason: Moderation runs Wolverine with TypeLoadMode.Static in production and uses pre-generated handler code in `CoffeePeek.ModerationService/Internal/Generated/WolverineHandlers/`. A new message type needs `dotnet run -- codegen write`; if that is skipped, production crash-loops (see `CoffeePeek.ModerationService.Tests/WolverinePreGeneratedHandlersTests.cs`). Adding a record parameter does not change the `Handle` signature, so the generated code is unaffected.
2. Spoofing safety: the `/mine` actions do NOT bind the query record from the query string. They take `page`, `pageSize`, `status` as separate `[FromQuery]` parameters (same as `ShopChangeRequestsController.GetMine`) and build the query with `UserId: userContext.GetUserIdOrThrow()`. On the moderator-only `GET` (which binds the record with `[FromQuery]`), `?userId=` becomes a moderator-only "filter by submitter" option. That is acceptable because moderators can already see every item. Mention this in the SUMMARY.
3. Contract DTO changes are additive and nullable only, so no versioning is needed:
   - `ModerationShopDto`: add `public string? RejectedReason { get; set; }` and `public Guid? PublishedShopId { get; set; }`.
   - `ModerationRoasterDto`: add `public string? RejectedReason { get; init; }`.
   - `ModerationReviewDto`: already has `ModerationStatus`, `RejectedReason`, `ModeratedAt`, `ModeratedBy`. No change.
   The domain stores a published id only for shops (`ModerationShop.ShopId`, set by `AddShopId` from `ModerationShopApproveCompleteHandler`; `Guid.Empty` until then). There is no stored published id for roasters or reviews, so none is added and there is no schema change. `ModerationShopDto` is an input to the moderator `PUT`, but `UpdateModerationCoffeeShopHandler` copies fields by hand, so the new fields cannot be written through that endpoint.
4. Gateway: no change needed. `CoffeePeek.Gateway/appsettings.json` routes `/api/ModerationShops/{**remainder}`, `/api/ModerationRoasters/{**remainder}`, `/api/ModerationReviews/{**remainder}` and `/api/ShopChangeRequests/{**remainder}` with no gateway AuthorizationPolicy. Auth is enforced by `[Authorize]` in the service, the same way the existing `ShopChangeRequests/mine` works. The `moderation-submission` rate limiter does not throttle GETs. Do not edit gateway config.
5. Route ordering: `[HttpGet("mine")]` does not clash with `[HttpGet("{id:guid}")]` because of the guid constraint.
6. No DB migration. If the executor finds that one seems necessary, STOP and report instead of adding it.
</design_decisions>

<interfaces>
Current repository signatures (Domain), which gain a trailing `Guid? userId = null` parameter placed BEFORE the CancellationToken:

IQueryModerationShopRepository:
  Task<(IReadOnlyList<ModerationShop> Items, int TotalCount)> GetPagedForReviewAsync(int page, int pageSize, ModerationStatus? status, string? search, CoffeeFocus? coffeeFocus, CancellationToken cancellationToken = default);
IQueryModerationRoasterRepository:
  Task<(IReadOnlyList<ModerationRoaster> Items, int TotalCount)> GetPagedForReviewAsync(int page, int pageSize, ModerationStatus? status, CancellationToken ct = default);
IQueryModerationReviewRepository:
  Task<(ModerationReview[] Items, int TotalCount)> GetPagedAsync(int page, int pageSize, ModerationStatus? status, string? search, CancellationToken cancellationToken = default);

Domain user fields: ModerationShop.UserId (Guid?), ModerationRoaster.UserId (Guid?), ModerationReview.UserId (Guid, non-null).
Domain rejection fields: RejectedReason (string?) on all three. ModerationShop.ShopId (Guid, Guid.Empty until published).

Current query records (Application):
  GetAllModerationShopsQuery([Range(1, int.MaxValue)] int Page = 1, [Range(1, 100)] int PageSize = 20, ModerationStatus? Status = null, CoffeeShopType? Type = null, [StringLength(100)] string? Search = null)
  GetAllModerationRoastersQuery([Range(1, int.MaxValue)] int Page = 1, [Range(1, 100)] int PageSize = 20, ModerationStatus? Status = null)
  GetAllModerationReviewsQuery([Range(1, int.MaxValue)] int Page = 1, [Range(1, 100)] int PageSize = 20, ModerationStatus? Status = null, [StringLength(100)] string? Search = null)
Responses: GetAllModerationShopsResponse(ModerationShopDto[] ModerationShops, ...), GetAllModerationRoastersResponse(ModerationRoasterDto[] Items, ...), GetAllModerationReviewsResponse(ModerationReviewDto[] ReviewDtos, ...), each followed by TotalItems, TotalPages, CurrentPage, PageSize.

Factories for tests:
  ModerationShop.Create(string name, Guid? userId, Guid cityId, ...)  (existing test: ModerationShop.Create("Test Cafe", Guid.NewGuid(), Guid.NewGuid(), null))
  ModerationRoaster.Create(string name, Guid? userId, string? about); roaster.Approve(); roaster.Reject(reason)
  ModerationReview.Create(userId, shopId, moderationShopId?, userName, header, comment, 4, 4, 4, photos[])  (see ModerationReviewRepositoryTests)
  MapsterConfiguration.CreateMapper(new MediaPublicUrlOptions())  (CoffeePeek.Moderation.Application.Mapper)
  Reject reason length must be between BusinessConstants.MinRejectReasonCommentLength and MaxRejectReasonCommentLength; use a reason of about 30 characters.
</interfaces>
</context>

<tasks>

<task type="auto" tdd="true">
  <name>Task 1: User filter in the three moderation query repositories</name>
  <files>CoffeePeek.Moderation.Domain/Aggregates/ModerationShopAggregate/Repositories/IQueryModerationShopRepository.cs, CoffeePeek.Moderation.Domain/Aggregates/ModerationRoasterAggregate/Repositories/IQueryModerationRoasterRepository.cs, CoffeePeek.Moderation.Domain/Aggregates/ModerationReviewAggregate/Repositories/IQueryModerationReviewRepository.cs, CoffeeShop.Moderation.Persistence/Repositories/QueryModerationShopRepository.cs, CoffeeShop.Moderation.Persistence/Repositories/QueryModerationRoasterRepository.cs, CoffeeShop.Moderation.Persistence/Repositories/QueryModerationReviewRepository.cs, CoffeePeek.ModerationService.Tests/ModerationMineRepositoryFilterTests.cs</files>
  <behavior>
    - Roaster: seed 2 roasters for user A, 1 for user B and 1 anonymous (null UserId). GetPagedForReviewAsync(1, 20, null, userId: A) returns exactly A's 2 with TotalCount 2.
    - Roaster: userId A plus status Rejected (reject one of A's) returns only that one.
    - Roaster: userId null returns all 4 (existing moderator behavior unchanged).
    - Review: seed reviews for users A and B. GetPagedAsync(1, 20, null, null, userId: A) returns only A's reviews.
    - Shop: seed shops for A, B and anonymous. GetPagedForReviewAsync(..., userId: A) returns only A's. If the EF InMemory provider rejects the `AsSplitQuery()` in BuildReviewQuery, drop only the shop case and note it in the SUMMARY. Do not change the production query to suit the test provider.
  </behavior>
  <action>
    Write the tests first in the new file `CoffeePeek.ModerationService.Tests/ModerationMineRepositoryFilterTests.cs`. Follow the `ModerationReviewRepositoryTests` style: `UseInMemoryDatabase(Guid.NewGuid().ToString())`, seed with one `ModerationDbContext`, query with a fresh one, use `TestContext.Current.CancellationToken`, assert with FluentAssertions. Run the tests and confirm they fail to compile or fail (RED).

    Then add a trailing optional `Guid? userId = null` parameter, placed immediately before the CancellationToken parameter, to the three Domain interface methods listed in `<interfaces>` and to their Persistence implementations. In each implementation, add `if (userId.HasValue) query = query.Where(x => x.UserId == userId.Value);` next to the existing status filter. Leave ordering (CreatedAtUtc desc), Skip/Take and Includes as they are. For reviews, `UserId` is a non-nullable Guid, so compare directly. The review query already excludes nothing by IsSoftDelete. Keep that behavior and do not add a soft-delete filter in this task.

    Existing callers pass the CancellationToken positionally (for example `GetPagedForReviewAsync(query.Page, query.PageSize, domainStatus, ct)`). Once the new parameter sits before the token, those calls stop compiling or bind wrongly. Task 2 fixes those handlers, so for this task use named `ct:` / `cancellationToken:` arguments where needed, or do Task 1 and Task 2 together before running a full build. The build must be green when Task 2 ends.
  </action>
  <verify>
    <automated>dotnet test CoffeePeek.ModerationService.Tests --filter "FullyQualifiedName~ModerationMineRepositoryFilterTests"</automated>
  </verify>
  <done>All three repositories filter by userId when it is supplied, return everything when it is null, and the new repository tests pass.</done>
</task>

<task type="auto" tdd="true">
  <name>Task 2: Forward UserId through the GetAll queries and handlers, and expose rejection reason and published id</name>
  <files>CoffeePeek.Moderation.Application/Features/Shop/GetAllModerationShops/GetAllModerationShopsQuery.cs, CoffeePeek.Moderation.Application/Features/Shop/GetAllModerationShops/GetAllModerationShopsHandler.cs, CoffeePeek.Moderation.Application/Features/Roaster/GetAllModerationRoasters/GetAllModerationRoastersQuery.cs, CoffeePeek.Moderation.Application/Features/Roaster/GetAllModerationRoasters/GetAllModerationRoastersHandler.cs, CoffeePeek.Moderation.Application/Features/Review/GetAllModerationReviews/GetAllModerationReviewsQuery.cs, CoffeePeek.Moderation.Application/Features/Review/GetAllModerationReviews/GetAllModerationReviewsHandler.cs, CoffeePeek.Contract/Dtos/CoffeeShop/ModerationShopDto.cs, CoffeePeek.Contract/Dtos/CoffeeShop/ModerationRoasterDto.cs, CoffeePeek.Moderation.Application/Mapper/MapsterConfiguration.ModerationShop.cs, CoffeePeek.Moderation.Application.Tests/Features/MyModerationSubmissionsHandlersTests.cs, CoffeePeek.ModerationService.Tests/ModerationMineDtoContractTests.cs</files>
  <behavior>
    - Shop handler: given GetAllModerationShopsQuery(UserId: X), the repository mock's GetPagedForReviewAsync is called once with userId == X. Given the default query, it is called with userId == null.
    - Roaster handler: same check with GetAllModerationRoastersQuery(Status: Rejected, UserId: X). The repository receives status Rejected (domain enum) and userId X.
    - Review handler: same check with GetAllModerationReviewsQuery(UserId: X).
    - Paging math is unchanged: with repository TotalCount 45 and PageSize 20, the response has TotalPages 3, CurrentPage and PageSize echoed.
    - Mapping (real Mapster via MapsterConfiguration.CreateMapper(new MediaPublicUrlOptions())): a rejected ModerationRoaster maps to ModerationRoasterDto with RejectedReason set and ModerationStatus Rejected.
    - Mapping: a ModerationShop whose ShopId is Guid.Empty maps to PublishedShopId == null. After shop.AddShopId(id) it maps to PublishedShopId == id. A rejected shop maps RejectedReason. Serialized camelCase JSON contains "rejectedReason" and "publishedShopId".
  </behavior>
  <action>
    Write the tests first. Put handler tests in the new file `CoffeePeek.Moderation.Application.Tests/Features/MyModerationSubmissionsHandlersTests.cs`, using Moq for the `IQueryModeration*Repository` and `IMapper` in the same style as `ModerationStatusHandlersTests`. Call the static `Handle` methods directly. Put mapping and serialization tests in the new file `CoffeePeek.ModerationService.Tests/ModerationMineDtoContractTests.cs`, following `ModerationRoasterDtoContractTests` with the real mapper and `JsonSerializerDefaults.Web`. Confirm RED.

    Implementation, per design decision 1: append `Guid? UserId = null` as the last positional parameter of `GetAllModerationShopsQuery`, `GetAllModerationRoastersQuery` and `GetAllModerationReviewsQuery`. In each handler, pass `query.UserId` to the repository's new `userId` parameter and keep passing `ct`. Do not create new query or handler types, because they would need Wolverine codegen regeneration.

    Per design decision 3: add `public string? RejectedReason { get; set; }` and `public Guid? PublishedShopId { get; set; }` to `ModerationShopDto`, each with a short `///` doc comment ("Moderator's rejection reason; null unless rejected" / "Id of the published Shops-service coffee shop; null until approval completes"). Add `public string? RejectedReason { get; init; }` with a doc comment to `ModerationRoasterDto`. RejectedReason maps by name automatically. In `MapsterConfiguration.ModerationShop.cs`, inside the existing `NewConfig<ModerationShop, ModerationShopDto>()` chain, add `.Map(d => d.PublishedShopId, s => s.ShopId == Guid.Empty ? (Guid?)null : s.ShopId)`. Do not touch the `ModerationShop -> ShopDto` config or `ModerationReviewDto`.

    Fix any compile errors in other callers caused by Task 1's signature change. The known callers are the three GetAll handlers.
  </action>
  <verify>
    <automated>dotnet build CoffeePeek.slnx && dotnet test CoffeePeek.Moderation.Application.Tests --filter "FullyQualifiedName~MyModerationSubmissionsHandlersTests" && dotnet test CoffeePeek.ModerationService.Tests --filter "FullyQualifiedName~ModerationMineDtoContractTests|FullyQualifiedName~ModerationRoasterDtoContractTests|FullyQualifiedName~ModerationReviewDtoContractTests"</automated>
  </verify>
  <done>The solution builds. The handlers forward UserId. The DTOs expose RejectedReason (shop, roaster) and PublishedShopId (shop). The new and existing contract tests pass.</done>
</task>

<task type="auto">
  <name>Task 3: GET /mine actions on the three controllers, plus client API doc</name>
  <files>CoffeePeek.ModerationService/Controllers/ModerationShopsController.cs, CoffeePeek.ModerationService/Controllers/ModerationRoastersController.cs, CoffeePeek.ModerationService/Controllers/ModerationReviewsController.cs, docs/api/moderation-mine.md</files>
  <action>
    In each of the three controllers, add a `GetMine` action directly after the existing moderator `GetAll...` action, attributed `[HttpGet("mine")]`, `[Authorize]` (plain authenticated, NOT `Policy = RoleConsts.Moderator`), a `[Description(...)]` where that controller already uses Description, `[ProducesResponseType<Response<...Response>>(StatusCodes.Status200OK)]` and `[ProducesResponseType<ErrorResponse>(StatusCodes.Status401Unauthorized)]`.

    Parameters, per design decision 2 and mirroring `ShopChangeRequestsController.GetMine`: `[FromQuery, Range(1, int.MaxValue)] int page = 1`, `[FromQuery, Range(1, 100)] int pageSize = 20`, `[FromQuery] ModerationStatus? status = null`, `CancellationToken ct = default`. Do NOT accept the query record, a userId, or search/type params. `ModerationReviewsController` needs `using System.ComponentModel.DataAnnotations;` for Range.

    Build the query explicitly:
    - `new GetAllModerationShopsQuery(page, pageSize, status, UserId: userContext.GetUserIdOrThrow())`
    - `new GetAllModerationRoastersQuery(page, pageSize, status, UserId: userContext.GetUserIdOrThrow())`
    - `new GetAllModerationReviewsQuery(page, pageSize, status, UserId: userContext.GetUserIdOrThrow())`

    Invoke via `bus.InvokeAsync<Response<...Response>>`, add the same four X-Total-Count / X-Total-Pages / X-Current-Page / X-Page-Size headers as the sibling GetAll action, and return `Ok(response)`. To avoid copy-pasting the header block a fourth time per controller, you may extract a private `AddPagingHeaders(int totalItems, int totalPages, int currentPage, int pageSize)` helper in each controller and use it from both GetAll and GetMine. This is optional; if you do it, keep the behavior identical. Do not change the existing moderator actions' authorization.

    Do not modify `CoffeePeek.Gateway/appsettings.json` (design decision 4). Do not regenerate Wolverine code (no new message types).

    Create `docs/api/moderation-mine.md` for client developers, following the tone and structure of `docs/api/photos-client.md`. Cover:
    - an intro saying anonymous submissions are not listed.
    - auth: any authenticated user JWT; 401 without one.
    - shared query params (page 1+, pageSize 1-100, status as ModerationStatus int values; confirm the numeric values from `CoffeePeek.Contract/Enums/ModerationStatus.cs`, e.g. Approved = 1 per the existing contract test) and the four paging headers.
    - one section each for `GET /api/ModerationShops/mine` (data.moderationShops[], highlighting moderationStatus, rejectedReason, publishedShopId), `GET /api/ModerationRoasters/mine` (data.items[], moderationStatus, rejectedReason; no published id is stored), `GET /api/ModerationReviews/mine` (data.reviewDtos[], moderationStatus, rejectedReason, moderatedAt, shopId).
    - `GET /api/ShopChangeRequests/mine`: the existing endpoint. Read its extra filters (shopId, section) and DTO fields from `ShopChangeRequestsController` and `ShopChangeRequestDto` and describe them accurately.
    Each section gets a short example JSON envelope in camelCase. Do not edit `docs/api/admin-stats.md`; it covers admin-only endpoints.
  </action>
  <verify>
    <automated>dotnet build CoffeePeek.slnx && dotnet test CoffeePeek.ModerationService.Tests && dotnet test CoffeePeek.Moderation.Application.Tests && grep -c "HttpGet(\"mine\")" CoffeePeek.ModerationService/Controllers/ModerationShopsController.cs CoffeePeek.ModerationService/Controllers/ModerationRoastersController.cs CoffeePeek.ModerationService/Controllers/ModerationReviewsController.cs && test -f docs/api/moderation-mine.md</automated>
  </verify>
  <done>Each of the three controllers has exactly one `[HttpGet("mine")]` `[Authorize]` action whose user id comes only from `userContext.GetUserIdOrThrow()`. The full ModerationService.Tests suite passes, including WolverinePreGeneratedHandlersTests and OpenApiDocumentGenerationTests. `docs/api/moderation-mine.md` documents all four /mine endpoints.</done>
</task>

</tasks>

<threat_model>
## Trust Boundaries

| Boundary | Description |
|----------|-------------|
| client -> Gateway -> ModerationService | Untrusted query params (page, pageSize, status). User identity arrives via gateway-validated auth headers read by IUserContext |

## STRIDE Threat Register

| Threat ID | Category | Component | Disposition | Mitigation Plan |
|-----------|----------|-----------|-------------|-----------------|
| T-gfl-01 | Information disclosure / Spoofing | GET */mine | mitigate | /mine actions take no userId input and do not bind the query record; UserId is set only from `userContext.GetUserIdOrThrow()` (throws, giving 401/403, if absent) |
| T-gfl-02 | Elevation of privilege | Moderator GetAll `?userId=` | accept | Moderator-only endpoint whose callers already see every item; the extra filter reveals nothing new |
| T-gfl-03 | Denial of service | pageSize | mitigate | `[Range(1, 100)]` on pageSize for /mine, the same cap as the existing queries; GETs are unthrottled at the gateway, as for the existing GetAll |
| T-gfl-04 | Information disclosure | ModerationShopDto new fields | accept | RejectedReason and PublishedShopId are shown only to the submitter or to moderators; they contain no PII |
</threat_model>

<verification>
- `dotnet build CoffeePeek.slnx` succeeds.
- `dotnet test CoffeePeek.ModerationService.Tests` and `dotnet test CoffeePeek.Moderation.Application.Tests` pass, including WolverinePreGeneratedHandlersTests (proving no codegen regeneration was needed).
- No new files under `CoffeeShop.Moderation.Persistence/Migrations/`.
- `git diff --stat -- CoffeePeek.Gateway` is empty.
</verification>

<success_criteria>
- Three new `GET .../mine` endpoints return only the caller's own submissions, filterable by status and paged with the standard headers.
- Shop items expose moderationStatus, rejectedReason and publishedShopId. Roaster items expose moderationStatus and rejectedReason. Review items already expose moderationStatus and rejectedReason.
- Repository, handler and mapping tests guard the user filter and the new fields.
- `docs/api/moderation-mine.md` documents all four /mine endpoints, including the existing ShopChangeRequests one.
</success_criteria>

<output>
Create `.planning/quick/260925-gfl-user-mine-endpoints-for-moderation-submi/260925-gfl-SUMMARY.md` when done. Note in it: the moderator GetAll endpoints now also accept `?userId=` (design decision 2), and whether the shop repository InMemory test was kept.
</output>
