---
phase: 04-public-profiles-in-account
plan: 02
subsystem: api
tags: [slugs, public-addresses, account, user-profiles, privacy, controllers, backfill]

requires:
  - phase: 04-public-profiles-in-account
    provides: "Plan 04-01: UserPublicAddresses registry, ProfileAddressWriter (Stem, owner-aware FindCandidateAsync), ProfileAddressTransaction, AccountDbContext.PublicAddressesEnabled"
  - phase: 02-shop-roaster-public-addresses
    provides: Shops PublicAddressService and public/admin address controller patterns (mirrored, reduced to one kind)
provides:
  - IProfileAddressService (resolve, batch, bounded backfill) with the visibility-filtered ProfileAddressService, registered scoped
  - Anonymous NoStore endpoints GET api/Users/by-slug/{slug}, GET api/Users/public-addresses?ids=, GET api/Users/{id:guid}/public-address
  - Admin-only POST api/admin/users/public-addresses/backfill?afterId&limit&dryRun (the only admin address action for users)
  - D-04 fix — GET api/Users/{id:guid} returns 404 for soft-deleted and blocked users
affects: [04-03-gateway-docs-verification, frontend-phase-6-profile-urls, shops-account-user-existence-lookup, live-postgresql-gate]

tech-stack:
  added: []
  patterns:
    - "Address metadata read directly from a read-only service (no new Wolverine handler); profile data still through IMessageBus with the existing command"
    - "NotFoundException from the data read caught in the controller and turned into NotFound(), so a user hidden between resolve and read looks exactly like an unknown slug"
    - "Reflection tests select overloaded actions by parameter types (UsersController.GetById(Guid?) vs GetById())"

key-files:
  created:
    - CoffeePeek.Account.Application/Common/Interfaces/IProfileAddressService.cs
    - CoffeePeek.Account.Persistence/PublicAddresses/ProfileAddressService.cs
    - CoffeePeek.AccountService/Controllers/PublicUserAddressesController.cs
    - CoffeePeek.AccountService/Controllers/AdminUserPublicAddressesController.cs
    - CoffeePeek.Account.Infrastructure.Tests/Persistence/UserPublicAddressServiceTests.cs
    - CoffeePeek.Account.Infrastructure.Tests/PublicUserAddressControllerTests.cs
  modified:
    - CoffeePeek.Account.Persistence/DependencyInjection.cs
    - CoffeePeek.Account.Application/Features/User/GetProfile/GetPublicUserProfileHandler.cs
    - CoffeePeek.Account.Application.Tests/Features/User/GetProfile/GetProfileHandlerTests.cs

key-decisions:
  - "Profile visibility is !IsSoftDelete && !IsBlocked, checked live on resolve, batch and the profile data read; email confirmation is not required (D-03)"
  - "GET api/Users/{id:guid} now throws the same NotFoundException(\"User not found\") for soft-deleted and blocked users as for unknown ids; body-only change, pre-generated Wolverine handler unchanged (D-04)"
  - "Public address controllers bypass IMessageBus for address metadata (read-only IProfileAddressService, no new handler, no codegen drift) but read profile data through the existing GetPublicUserProfileCommand (one mapping path, D-04 re-check)"
  - "Admin surface for user addresses is backfill only under the Admin policy; no initialize, rename or history (D-06, D-10)"
  - "Shops AccountUserExistenceLookup (GET /api/Users/{userId}, 404 means not exists) now means exists and visible; no Shops code change"

patterns-established:
  - "Every hidden profile case returns the identical 404 with the framework's generic ProblemDetails body (no user data)"
  - "canonicalPath is built only from the literal /users/ plus the stored slug (D-08)"

requirements-completed: [D-03, D-04, D-06, D-08, D-10, P4-03, P4-05, P4-06]

duration: 15min
completed: 2026-09-29
---

# Phase 4 Plan 02: Profile Address Service, Public api/Users Endpoints and GUID-Route Privacy Fix Summary

**`ProfileAddressService` resolves profile slugs and aliases only for users who are not soft-deleted and not blocked. Three anonymous NoStore `api/Users` endpoints and one Admin-only backfill expose it. The existing `GET api/Users/{id:guid}` route no longer returns deleted or blocked profiles.**

## Performance

- **Duration:** about 15 min of active execution, over two sessions. The first executor was stopped by a rate limit partway through Task 2.
- **Started:** 2026-09-28T14:00:00Z
- **Completed:** 2026-09-29T07:08:00Z
- **Tasks:** 2 of 2 (TDD: 4 commits)
- **Files:** 9 (6 created, 3 modified)

## Accomplishments

- **Lookup service (Task 1).** `IProfileAddressService` / `ProfileAddressService`:
  - `ResolveAsync` matches case-insensitively. An unknown slug, a tombstone or a hidden owner returns null. Otherwise it returns the owner's current canonical, with `IsAlias` set when the request used an old slug.
  - `GetByIdsAsync` accepts 1–100 non-empty ids and returns visible canonical addresses in input order.
  - `BackfillAsync` pages by id cursor and supports dry-run. A replay never renames and reports `AlreadyInitialized`. Soft-deleted users are excluded; blocked users are included (allocated but hidden). Unique violations on `IX_UserPublicAddresses_*` become 409.
  - All three methods return 503 while `PublicAddresses:Enabled` is false.
  - Registered with `AddScoped<IProfileAddressService, ProfileAddressService>()`.
- **D-04 privacy fix (Task 2).** `GetPublicUserProfileHandler` now checks `if (user is null || user.IsSoftDelete || user.IsBlocked) throw new NotFoundException("User not found");`. Only the method body changed. The signature, command, response DTO, Mapster mapping and `GetPublicUserProfileCommandHandler1926808727` are unchanged, and `WolverinePreGeneratedHandlersTests` stays green.
- **Public endpoints.** `PublicUserAddressesController` has `[Route("api/Users")]`, no Authorize attribute and `ResponseCache(NoStore, None)`:
  - `by-slug/{slug}` returns `PublicAddressEnvelope<PublicUserProfileResponse>`. The profile is read with `new GetPublicUserProfileCommand(address.EntityId)`, the resolved owner id.
  - `public-addresses?ids=` returns `PublicAddressMetadata[]`.
  - `{id:guid}/public-address` returns `PublicAddressMetadata`, or 404.
  - Every hidden case returns the same 404 with the framework's generic ProblemDetails body (no user data). This covers unresolved slugs, a `NotFoundException` from the data read and an error response. Invalid input returns 400 and a disabled feature returns 503.
  - The templates don't collide with `UsersController`. `{id:guid}` cannot match `public-addresses`, and `by-slug/{slug}` has two segments. A reflection test asserts that no existing template is shadowed.
- **Admin endpoint.** `AdminUserPublicAddressesController` has `[Route("api/admin/users/public-addresses")]` and `[Authorize(Policy = RoleConsts.Admin)]`. Its only action is `POST backfill?afterId&limit=50&dryRun=true`.

## Why address metadata bypasses IMessageBus

Address metadata comes straight from the read-only `IProfileAddressService`, as in the phase 2 and 3 public address controllers. A new Wolverine handler would change the pre-generated code under `CoffeePeek.AccountService/Internal/Generated/`; this way nothing there changes. Profile data still goes through `IMessageBus` with the existing `GetPublicUserProfileCommand`. That keeps one mapping path and re-checks visibility (D-04) at read time. A user deleted or blocked between the resolve and the read therefore also gets the identical 404.

## D-04 behavior change

**`GET api/Users/{id:guid}` now returns 404 for soft-deleted and blocked users.** The exception message is the same as for an unknown id, so the three cases can't be told apart. Visible users get the same `Response<PublicUserProfileResponse>` DTO as before.

Consequence for Shops: `AccountUserExistenceLookup` calls `GET /api/Users/{userId}` and treats 404 as "does not exist". It now effectively means "exists and visible", i.e. not deleted and not blocked. No Shops code was changed. If a Shops flow needs to see blocked users as existing, it will need a dedicated internal endpoint; that is out of scope here.

## Task Commits

1. **Task 1: ProfileAddressService, interface, DI and service tests**
   - RED `94a7fdc4`: `test(04-02): add failing profile address service tests` (compile failure: the service was missing)
   - GREEN `359a7900`: `feat(04-02): add visibility-filtered profile address service and backfill`
2. **Task 2: GUID-route leak fix, public api/Users endpoints, Admin backfill endpoint, contract and behavior tests**
   - RED (handler facts) landed in `21419ba3` (user docs commit; see the anomaly note below)
   - RED `9705063a`: `test(04-02): add failing public user address controller tests`
   - GREEN `bd2b746d`: `feat(04-02): add public user address endpoints and close GUID-route leak`

No refactor commits were needed. No packages were added and no tracked file was deleted.

### Commit anomaly: partial RED tests in `21419ba3`

The first executor was stopped by a rate limit during Task 2 RED. By then it had added the two D-04 handler facts (`Handle_WhenUserSoftDeleted_ThrowsNotFoundAndDoesNotMap`, `Handle_WhenUserBlocked_ThrowsNotFoundAndDoesNotMap`) and the `AssertHiddenAsync` helper to `GetProfileHandlerTests.cs`. These uncommitted changes were then swept into the user's docs commit `21419ba3` ("Add validation strategy documentation for public profiles in account phase 4"). History was not rewritten.

On resume I checked the facts against the plan. They match the behavior block exactly: the mapper is set up to succeed, the message is "User not found", and `Map` is verified never called. I kept them unchanged. `21419ba3` is effectively the RED commit for the handler part of Task 2, and `9705063a` is the RED commit for the controller part.

## Tests

- **`UserPublicAddressServiceTests`** (7 InMemory facts, Task 1):
  - Canonical, alias and case-insensitive resolve, with `/users/` paths; unconfirmed email stays visible.
  - A blocked user is hidden everywhere, and unblocking restores the same address and revision.
  - A soft-deleted user never resolves, including a leftover canonical row. The ghost row is asserted to exist first, so the check can't pass vacuously.
  - Batch returns visible canonicals in input order and validates ids.
  - Invalid slugs return 400; a disabled feature returns 503 on all three methods.
  - Backfill: dry run, cursor, replay, deleted excluded, blocked included.
- **`GetPublicUserProfileHandlerTests`** (+2 facts): soft-deleted and blocked users throw `NotFoundException("User not found")`, and the mapper is never called.
- **`PublicUserAddressControllerTests`** (10 facts):
  - Reflection: routes and templates, no `UsersController` template collision, `UsersController.GetById(Guid?)` still `{id:guid}`. Both controllers are NoStore/None. The public controller has no Authorize attribute. The admin controller has the Admin policy, no AllowAnonymous, exactly one action `Backfill` with `HttpPost("backfill")`, and no `AdminUsersController` template starting with `public-addresses`.
  - Behavior: unresolved slug returns `NotFoundResult` and the bus is never called. Hidden after resolve returns `NotFoundResult`, the exact type, not an ObjectResult. An error response returns `NotFoundResult`. A visible user returns the envelope, with the command's `UserId` equal to the resolved owner id and no `Email` or `PhoneNumber` on the DTO. GUID lookup is called with exactly `[id]` and returns 404 or the metadata. Batch ids pass through, and backfill passes cursor, limit and dryRun through.

**RED evidence for Task 2.** The Application tests had 2 failing (the new handler facts) and 214 passing. The controller tests failed to compile because `PublicUserAddressesController` was missing.

## Verification

| Check | Result |
|---|---|
| `CoffeePeek.Account.Application.Tests` | 216 passed, 0 failed (baseline 214, +2) |
| `CoffeePeek.Account.Infrastructure.Tests` | 89 passed, 0 failed (72 after 04-01, +7 service, +10 controller), including `WolverinePreGeneratedHandlersTests.ProductionAccountHost_StartsPastWolverineHandlerCheck` |
| `CoffeePeek.Account.Domain.Tests` | 255 passed, 0 failed |
| `dotnet build CoffeePeek.Backend.ci.slnf -c Release` | 0 errors; no warnings from new or changed files |
| Handler body-only gate (`Handle(`, `IUserRepository`, `IMapper`, `CancellationToken`, `class ` in the diff) | 0 |
| `user.IsSoftDelete \|\| user.IsBlocked` in handler | 1 |
| Unchanged since `96acf741`: `AccountService/Internal/Generated`, `UsersController.cs`, `AdminUsersController.cs`, `GetPublicUserProfileCommand.cs`, `PublicUserProfileResponse.cs`, `Account.Application/Mapper`, `CoffeePeek.Contract`, `CoffeePeek.Gateway`, `Shops.Persistance`, `ShopsService` | `git diff --quiet` exit 0 |
| Public controller gates | `Route("api/Users")` 1, `catch (NotFoundException)` 1, `new GetPublicUserProfileCommand(address.EntityId)` 1, `[Authorize` 0 |
| Admin controller gates | `Route("api/admin/users/public-addresses")` 1, `RoleConsts.Admin` 1, `[Http` 1 |
| Service gates | `AddScoped<IProfileAddressService, ProfileAddressService>` 1, `"/users/"` 1, `!u.IsSoftDelete && !u.IsBlocked` 2, `HistoryAsync\|ChangeAsync\|InitializeAsync` in interface 0 |
| `git diff --check` | clean |

**Not verified:** there is no live PostgreSQL, Docker, gateway or HTTP run. The endpoints are verified only by reflection and direct controller-call tests, and the service only by InMemory tests. Four things are therefore unproven:
- The ProblemDetails body that `[ApiController]` attaches to `NotFound()` at runtime.
- Model binding of `ids=` query arrays.
- The backfill unique-violation mapping to 409 under real concurrency.
- The advisory-lock transaction.

These stay live-gate items for plan 04-03. `PublicAddresses__Enabled` stays unset and false.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] The reflection helper was ambiguous for `UsersController.GetById`**
- **Found during:** Task 2 GREEN test run
- **Issue:** `UsersController` has two `GetById` overloads: `GetById(Guid?)` for `{id:guid}` and `GetById()` for `me`. `Type.GetMethod(name)` threw `AmbiguousMatchException`.
- **Fix:** `Template<T>(string method, params Type[] parameters)` now selects by parameter types when given. The test asserts on `GetById(Guid?)`. No production code changed.
- **Files modified:** `CoffeePeek.Account.Infrastructure.Tests/PublicUserAddressControllerTests.cs`
- **Commit:** `bd2b746d`

### Minor execution notes (no behavior impact)

- **Test name.** The plan names one fact `GetBySlug_ProfileHiddenAfterResolve_ReturnsEmptyNotFound`, and I kept that name so it traces to the plan. The fact asserts `NotFoundResult`, not an ObjectResult. Its comment explains that at runtime this is the framework's generic ProblemDetails 404, not an empty body.
- **Extra response metadata.** Some actions carry more `ProducesResponseType` attributes than the plan listed: 503 on the batch and GUID actions, and 400/409/503 on backfill. These are OpenAPI metadata only.
- **Handler comment.** The D-04 comment in the handler also notes that the message is identical to the unknown-id case.

## TDD Gate Compliance

- Task 1: RED `94a7fdc4` (`test(04-02)`), then GREEN `359a7900` (`feat(04-02)`).
- Task 2, controller part: RED `9705063a` (`test(04-02)`), then GREEN `bd2b746d` (`feat(04-02)`).
- **Warning:** the handler-fact RED for Task 2 has no `test(04-02)` commit. It landed in the user's non-conventional docs commit `21419ba3`, as explained above. It still comes before GREEN, and it was run and seen failing (2 of 2) before the fix.

## Known Stubs

None.

## Threat Flags

None. The new surface (three anonymous reads and one Admin write) is exactly what threats T-04-08 to T-04-15 cover. The one consequence outside this service, the changed meaning of the Shops `AccountUserExistenceLookup` 404, is recorded above.

## Next Phase Readiness

- **Plan 04-03:** add the dedicated gateway `by-slug` route with precedence tests, including the auth-endpoints rate limit (T-04-09). Document the user profile endpoints, the D-04 404 change and its Shops lookup consequence, and migration `20260928134755_AddUserPublicAddresses` as a deployment prerequisite. Extend the live gate with the items above.
- **Frontend (phase 6):** can link review authors through `GET api/Users/public-addresses?ids=` and render `/users/{slug}` pages through `by-slug`, redirecting when `isAlias` is true.

## Self-Check: PASSED

- All 6 created files and 3 modified files exist on disk.
- Commits `94a7fdc4`, `359a7900`, `21419ba3`, `9705063a` and `bd2b746d` are present in `git log`.
