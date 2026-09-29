---
phase: 04-public-profiles-in-account
verified: 2026-09-29T07:46:59Z
status: passed
score: 21/21 must-haves verified
overrides_applied: 0
infrastructure_gap: # Documented enablement gate (same precedent as phases 1-3: passed-with-infrastructure-gap). Not a phase gap: the feature is default-off and the gate is documented in docs/api/public-addresses.md "Enablement gate" steps 2 and 5-6.
  reason: "Live PostgreSQL, Docker, a running YARP gateway and HTTP are unavailable. Verification used InMemory behavior tests, the Npgsql design-time model, the ASP.NET Core routing precedence API and direct controller calls only."
  open_items:
    - "Unique IX_UserPublicAddresses_Slug and filtered IX_UserPublicAddresses_OwnerId under concurrent registrations (Ivan/ivan/Иван at once), serialized by advisory lock 7402100401"
    - "Rename demotion flush (SaveCoreAsync before promotion) never violates the filtered canonical owner index"
    - "FK_UserPublicAddresses_Users_OwnerId RESTRICT; hard-delete statement ordering (retire before DELETE Users)"
    - "CK_UserPublicAddresses_Slug/_Lifecycle/_Revision/_Timestamps reject invalid rows"
    - "Savepoint rollback inside Wolverine's EfCoreEnvelopeTransaction: username unique violation leaves the address unchanged; duplicate-email registration leaves no row; Google concurrent-creation retry after ChangeTracker.Clear()"
    - "Backfill cursor (Guid CompareTo / OrderBy uuid) translation and paging on PostgreSQL; concurrent unique violation maps to 409"
    - "Migration 20260928134755_AddUserPublicAddresses applies cleanly to cpaccountdb"
    - "HTTP/gateway smoke: /api/Users/by-slug/reviews reaches AccountService through YARP; ids= array binding; identical ProblemDetails 404 body for every hidden case"
    - "D-02 decision (accept slug reservation growth or add a username-change cooldown) before enabling AccountService"
  default_off_confirmed: "PublicAddressOptions.Enabled defaults false; bound only from section 'PublicAddresses' in Account.Persistence DI; git grep finds no PublicAddresses setting in any appsettings, deploy/ file or AppHost"
advisories: # Non-blocking; from 04-REVIEW.md, re-assessed against the codebase by the verifier
  - id: WR-01
    severity: warning
    summary: "docs/api/public-addresses.md:160 says 'nothing links a deleted user to former slugs'. That is false at the database level."
    goal_impact: "none on the goal clause 'without copying private profile data into tombstones'. D-03 ('tombstones carry no private profile data') and D-06 (no audit table, no history endpoint) both hold in code; see the WR-01 assessment below. The false statement is a documentation accuracy defect and should be corrected before AccountService enablement. Exposure today is zero: with the flag unset, no address row, and therefore no tombstone, is ever created."
  - id: WR-02
    severity: warning
    summary: "The advisory lock 7402100401 is taken inside Wolverine's EfCoreEnvelopeTransaction and held across Redis/SignalR calls in AdminDeleteUserHandler and ConfirmAccountDeletionHandler until the outer commit"
    goal_impact: "none on correctness. With the flag off, only account deletions take the lock, so deletions serialize against each other only. With the flag on, registrations and renames also queue behind a slow deletion. This is an availability risk and should be an enablement-gate item."
  - id: WR-03
    severity: warning
    summary: "auth-endpoints is one per-IP bucket shared with login, refresh and registration. Profile browsing can exhaust it."
    goal_impact: "none. D-07 locks this limit. The docs say the three lookups share the limit, but not that login and refresh share it too. Relevant for phase 6 (SSR/frontend)."
  - id: WR-04
    severity: warning
    summary: "PublicAddress.Transition throws when DateTime.UtcNow is earlier than the stored UpdatedAtUtc. The resulting non-BaseException returns 500 on deletion or rename."
    goal_impact: "none today. With the flag off, no rows exist, so RetireAllAsync returns before Retire. Once enabled, a clock step-back blocks deletion until the clock catches up. Clamp now to the newest stored UpdatedAtUtc before enablement."
  - id: WR-05
    severity: warning
    summary: "The 'Shops parity' model test compares the Account model with literal copies of the Shops SQL, so later Shops drift goes undetected"
    goal_impact: "none. The verifier compared UserPublicAddressConfiguration with Shops PublicAddressConfiguration.Configure<T>: they are statement-for-statement identical today. The plan prescribed literal copies (no Shops reference from Account)."
---

# Phase 4: Public profiles in Account Verification Report

**Phase Goal:** readable stable public profile addresses for Account users, reusing the shared slug rules (`CoffeePeek.Shared.Domain/PublicAddresses/PublicSlug.cs`) and the phase 1–3 registry/lifecycle/lookup/rename/history/backfill model inside the Account bounded context, with an explicit profile privacy rule (private or deleted profiles never resolve or leak) and a username → slug policy. Account deletion must retire addresses without copying private profile data into tombstones. Existing GUID routes and DTOs stay unchanged; feature default-off behind the live PostgreSQL gate. External frontend (phase 6) out of scope.
**Verified:** 2026-09-29T07:46:59Z
**Status:** passed, with the documented live-PostgreSQL infrastructure gap (phase 1–3 precedent)
**Re-verification:** No. This is the initial verification; no prior 04-VERIFICATION.md exists.

The roadmap has no `success_criteria` for phase 4 (`roadmap.get-phase 4` returns `[]`). The must-haves are therefore the 21 plan frontmatter truths: 04-01 has 9, 04-02 has 6 and 04-03 has 6. The coverage set is CONTEXT decisions D-01..D-10 plus research labels P4-01..P4-07, and every one of them is mapped below. Commits `5adff179..85f5b428` since `96acf741` were inspected, plus the two D-04 handler facts in the user docs commit `21419ba3`.

## Goal Achievement

### Observable Truths

| # | Truth (source) | Status | Evidence |
|---|---|---|---|
| 1 | D-01: an email or Google user created while enabled gets one canonical row (revision 1, slug from username). Collisions get `-<8hex>`; `place`/`user` stems get `user-<8hex>` (04-01) | VERIFIED | Hook: `AccountDbContext.SaveChangesAsync` routes to `ProfileAddressWriter.SaveAsync`, then `InitializeAsync`, then `FindCandidateAsync`. `Stem` maps "place"/"user" to ("user", 1) (ProfileAddressWriter.cs:34-35). Both creation paths reach the override: `RegisterUserHandler` and `ExternalAuthService`/`QueryUserRepository.Add` + `OAuthLoginHandler` save through `UnitOfWork`, and the generated handlers resolve the context by `GetRequiredService<AccountDbContext>`, so `IOptions` is injected. Tests `Register_AllocatesCanonicalFromUsername_ForEmailAndGoogleUsers` (`ivan-petrov`, Google `ivan-petrov1985`) and `Register_CollidingAndUnsupportedUsernames_GetDeterministicSuffixes` pass. |
| 2 | D-01: a username change is a rename. The old canonical becomes an alias, A->B->A restores the same row (revisions 1->2->3), and a same-slug change (including own suffix) is a no-op (04-01) | VERIFIED | `FollowUsernameAsync` handles this: `slug == current.Slug` returns 0; otherwise it runs `MakeAlias`, then `SaveCoreAsync(true)`, then `row.Restore` or `Create`. `FindCandidateAsync` is owner-aware (`row.OwnerId == ownerId`). Tests `Rename_FollowsUsername_AndRestoresOwnAlias_AtoBtoA` (same row Id, revision 3) and `Rename_CaseOrPunctuationOnly_IsNoOp_IncludingOwnSuffix` pass; the latter asserts no `Candidate(...,2)` row. |
| 3 | D-01: another user's alias or a tombstone forces the new owner's own suffix; reserved slugs are never reassigned (04-01) | VERIFIED | A row owned by someone else or with a null owner is skipped (writer:49). `Rename_ToAnotherUsersAliasOrTombstone_GetsOwnSuffix` covers both: Y gets `Candidate("petr", Y, 1)` while X keeps alias `petr`, and W gets `Candidate("olga", W, 1)` while `olga` stays a Tombstone. |
| 4 | D-03: soft delete (confirmation, admin) and hard delete retire every row even when disabled. Block/unblock change nothing. A soft-deleted user's username rewrite allocates nothing (04-01) | VERIFIED | `IsRetiring` fires on the Deleted state or an `IsSoftDelete` false->true transition (by OriginalValue). `SaveAsync` retires before the `if (!db.PublicAddressesEnabled)` return (writer:66-67). `NeedsAddress` requires `!IsSoftDelete`. Both deletion handlers load the user tracked (`UserRepository.GetById`), then `SetSoftDelete`, `Update` and save. The theory `Deletion_RetiresEveryAddress_EvenWhenDisabled(true/false)` passes (both rows Tombstone, OwnerId null, revision 3, 0 rows with the user id), as do `HardDelete_RetiresBeforeRemovingUser`, `DeletedUser_UsernameRewrite_AllocatesNothing` and the block/unblock steps of `UnrelatedUpdates_...`. |
| 5 | D-03: a registry row has exactly Id, OwnerId, Slug, State, Revision, CreatedAtUtc, UpdatedAtUtc and no navigation (04-01) | VERIFIED | `UserPublicAddress` is a bare clone of the base with no extra members. `PublicAddress` is not an `IAuditableEntity`, so `AuditInterceptor` never touches it. `Model_HasOnlyTheSevenBaseColumnsAndNoNavigation`, `Model_UserHasNoNavigationToRegistry` and `Model_RestrictsUserDeletionUntilRetiredWithoutNavigations` pass. |
| 6 | D-05: an Account-local single-kind registry with no Shops/Shared.Domain/Contract changes. Model tests pin collation C, max length 100, the concurrency token, the index set, the 4 checks with Shops-identical SQL and a RESTRICT FK to Users (04-01) | VERIFIED | `git diff --quiet 96acf741 HEAD` over Shops.*, ShopsService, Shared.Domain/Persistence/Kernel and Contract exits 0 (re-run by the verifier). The verifier read `UserPublicAddressConfiguration.Configure` and Shops `PublicAddressConfiguration.Configure<T>` side by side: identical statements and SQL. The 7 persistence facts pass. The parity test uses literals (WR-05, non-blocking). |
| 7 | D-06: no audit or history; the migration creates exactly one table (04-01) | VERIFIED | `20260928134755_AddUserPublicAddresses.cs` contains one `CreateTable`, 4 `CheckConstraint`, the Restrict FK and 3 `CreateIndex`; `Down` is only `DropTable`. `grep "class \w*Audit\|DbSet<\w*Audit"` over Account finds nothing. `has-pending-model-changes` (re-run) prints "No changes have been made to the model since the last migration." Earlier Account migrations are unchanged (diff exit 0). |
| 8 | Unrelated saves (About, statistics, block/unblock) never rename, allocate or retire, because detection uses OriginalValue, not IsModified (04-01) | VERIFIED | Writer lines 20-29 use `OriginalValue`; the only `IsModified` is in a comment. `UnrelatedUpdates_NeverRenameAllocateOrRetire` deliberately desynchronizes the canonical from the username slug first (disabled rename), so an IsModified detector would be observable. It passes. Every `IUserRepository.Update` caller loads through the tracked `UserRepository` (GetById/GetByEmail have no AsNoTracking). |
| 9 | Flag defaults false; with it off, registration and renames allocate nothing; no handler/repository/User entity changes (04-01) | VERIFIED | `PublicAddressOptions.Enabled` is a plain bool (default false), bound once via `Configure<PublicAddressOptions>(GetSection("PublicAddresses"))`. `Disabled_NoAllocation_ThenFirstEnabledRenameAllocates` passes. `git diff` since `96acf741` is empty for `Account.Persistence/Repositories`, `Account.Domain/Entities/UserAggregate` and `AccountService/Internal/Generated`. The only `Account.Application/Features` change is the intended 04-02 D-04 body edit. |
| 10 | D-03: Resolve/GetByIds return only non-deleted, non-blocked users. Unknown, retired, deleted, blocked and leftover rows are hidden; unconfirmed email resolves; unblock restores the same address (04-02) | VERIFIED | `IsVisibleAsync` and the batch filter both use `!u.IsSoftDelete && !u.IsBlocked` (ProfileAddressService.cs:45,107). A tombstone (`OwnerId` null) returns null (line 29). Tests `Resolve_CanonicalAliasCaseInsensitive_AndUnconfirmedEmailIsVisible`, `Blocked_IsHiddenEverywhere_AndUnblockRestoresSameAddress`, `SoftDeleted_NeverResolves_IncludingLeftoverCanonicalRow` (ghost row asserted to exist first) and `Batch_...` pass. |
| 11 | D-04: `GET api/Users/{id:guid}` throws `NotFoundException("User not found")` for soft-deleted and blocked users; signature, command, DTO, mapping and generated handler unchanged (04-02) | VERIFIED | The handler diff is exactly the condition `user is null \|\| user.IsSoftDelete \|\| user.IsBlocked` plus a comment. `GetPublicUserProfileCommand.cs`, `PublicUserProfileResponse.cs`, `Mapper/`, `Internal/Generated` and `UsersController.cs` are unchanged (diff exit 0). The two new handler facts, which set up a succeeding mapper and verify `Map` is never called, pass. `WolverinePreGeneratedHandlersTests` passes. |
| 12 | D-08: canonicalPath is always `/users/{canonical}` built from a literal; an alias returns isAlias true with the current canonical; matching is case-insensitive (04-02) | VERIFIED | `CanonicalPrefix = "/users/"` plus `row.Slug` (service:19,110); `normalized = slug.ToLowerInvariant()`. The test asserts `ResolveAsync("ivan-petrov")` after the rename returns `("petr", "/users/petr", 2, true)`, and that `"IVAN-PETROV"` resolves as canonical. |
| 13 | D-10: the only admin surface is `POST api/admin/users/public-addresses/backfill` (Admin): dry-run writes nothing, cursor by id, replay reports alreadyInitialized, deleted excluded, blocked included (04-02) | VERIFIED | `AdminUserPublicAddressesController` has `[Authorize(Policy = RoleConsts.Admin)]` and a single `[HttpPost("backfill")]`. `BackfillAsync` filters `!u.IsSoftDelete` and the id cursor. `Backfill_DryRunCursorReplay_ExcludesDeletedIncludesBlocked` passes (0 rows after dry runs, 4 after write, replay all `AlreadyInitialized`, the blocked slug does not resolve). |
| 14 | D-06: no admin initialize/rename/history for users; the admin controller has exactly one action (04-02) | VERIFIED | Reflection test `AdminBackfill_RequiresAdminPolicyAndIsTheOnlyAction` passes. `IProfileAddressService` has only Resolve/GetByIds/Backfill. `AdminUsersController` is unchanged. |
| 15 | The public by-slug, batch and legacy-GUID endpoints are anonymous and NoStore, return envelope/metadata with no email or phone, give an identical 404 for every hidden case, 400 on invalid input and 503 when disabled, and read profile data via the existing command for the resolved owner id (04-02) | VERIFIED | `PublicUserAddressesController` has `[Route("api/Users")]`, NoStore/None and no Authorize. Unresolved slugs, `NotFoundException` and error responses all return `NotFound()`. The command is `new GetPublicUserProfileCommand(address.EntityId)`. No template collides with `UsersController`, which the verifier checked by listing all route attributes. All 10 controller tests pass. The runtime ProblemDetails body is a live/HTTP gate item. |
| 16 | D-07: `account-Users-by-slug-route` sends `/api/Users/by-slug/{slug}` to account-cluster on auth-endpoints with no AuthorizationPolicy, outranks `shops-users-reviews-route`, and leaves batch/legacy on the catch-all (04-03) | VERIFIED | The gateway `appsettings.json` diff is 5 added lines and 0 removed. `UserBySlugRoute_OutranksShopsUserReviewsForEverySlug` first proves all three templates match `/api/Users/by-slug/reviews` (non-vacuous), then compares `RoutePrecedence.ComputeInbound`. The verifier listed every `/api/users*` gateway path: no other route competes. The YARP runtime smoke is a gate item. |
| 17 | D-02: slug squatting is an explicit enablement-gate item; no cooldown is implemented (04-03) | VERIFIED | Docs enablement step 5 (line 37) and "Do not enable AccountService before step 5 is decided" (line 40). `grep UsernameChangedAtUtc` over Account.Domain/Persistence finds nothing. |
| 18 | D-09: the docs state that Google email-derived usernames become crawlable and that the frontend may noindex (04-03) | VERIFIED | docs line 164. |
| 19 | The migration is a deployment prerequisite independent of the flag; the flag stays unset in JSON, env and AppHost; the live gate lists the Account items as pending (04-03) | VERIFIED | Docs line 9 names `20260928134755_AddUserPublicAddresses`. `deploy/scripts/apply-migrations.sh:57-60` migrates Account first, and `ci-deploy.sh`/`deploy.sh` call it unless `SKIP_MIGRATIONS=1`. Docs lines 23-34 say "pending … none of them has been verified". A repo-wide `git grep` for `PublicAddresses(__\|:)` finds only the options doc comment and the two DI bindings. |
| 20 | The docs describe D-01, D-03, D-04, D-06, D-08, D-10 (04-03) | VERIFIED | `## User profiles` (lines 135-166), Admin API (191-197), Bounded backfill (212), Public API row and alias example (50, 90-111). One sentence in the D-06 paragraph (line 160) overstates unlinkability (WR-01): a documentation accuracy warning, not a missing description. |
| 21 | Phase gate: CI build, the four affected test projects, has-pending-model-changes and the isolation diff pass (04-03) | VERIFIED | The verifier re-ran these in Release with `--no-build` on binaries built after the last code commit (10:15 vs 05de23df at 10:11). Account.Domain 255/255 (baseline 231), Account.Application 216/216 (214), Account.Infrastructure 89/89 (54), Gateway 56/56 (54). Debug runs match. Drift is clean, the isolation diff and `git diff --check` exit 0, and there is no Release-only `TreatWarningsAsErrors`. The orchestrator's Debug CI slnf build had 0 errors. |

**Score:** 21/21 truths verified

### Required Artifacts

| Artifact | Expected | Status | Details |
|---|---|---|---|
| `CoffeePeek.Account.Domain/Entities/PublicAddressAggregate/UserPublicAddress.cs` | Typed registry row | VERIFIED | `public sealed class UserPublicAddress : PublicAddress`, EF ctor plus `Create`; used by writer, service and config |
| `CoffeePeek.Account.Application/Common/Options/PublicAddressOptions.cs` | Flag, default false | VERIFIED | Bound in Account.Persistence DI; read by `AccountDbContext.PublicAddressesEnabled` |
| `CoffeePeek.Account.Persistence/Configuration/UserPublicAddressConfiguration.cs` | Schema, checks, RESTRICT FK | VERIFIED | `DeleteBehavior.Restrict`, `HasOne<User>().WithMany()` with no navigation; picked up by `ApplyConfigurationsFromAssembly` |
| `CoffeePeek.Account.Persistence/Migrations/20260928134755_AddUserPublicAddresses.cs` (+ Designer, snapshot) | 1 table, 3 indexes, 4 checks, FK | VERIFIED | Exact shape; drift clean |
| `CoffeePeek.Account.Persistence/PublicAddresses/ProfileAddressWriter.cs` | Retire/allocate/follow, owner-aware candidates | VERIFIED | 129 lines; `OriginalValue` ×2; `SaveCoreAsync` only (no `SaveChangesAsync` re-entry) |
| `CoffeePeek.Account.Persistence/PublicAddresses/ProfileAddressTransaction.cs` | Savepoint/owned transaction plus lock | VERIFIED | Line-for-line copy of the Shops helper; `pg_advisory_xact_lock(7402100401)`; `ChangeTracker.Clear()` in finally |
| `CoffeePeek.Account.Persistence/Configuration/AccountDbContext.cs` | Hook, optional options ctor | VERIFIED | Gate `IsRetiring \|\| (Enabled && NeedsAddress)`; `SaveCoreAsync` internal base save |
| `CoffeePeek.Account.Application/Common/Interfaces/IProfileAddressService.cs` | Resolve/batch/backfill contract | VERIFIED | 3 members; no Initialize/Change/History |
| `CoffeePeek.Account.Persistence/PublicAddresses/ProfileAddressService.cs` | Visibility-filtered lookup plus backfill | VERIFIED | `!u.IsSoftDelete && !u.IsBlocked` ×2; `"/users/"` literal; 409 mapping for `IX_UserPublicAddresses_*` |
| `CoffeePeek.AccountService/Controllers/PublicUserAddressesController.cs` | Anonymous api/Users endpoints | VERIFIED | Route, NoStore, `catch (NotFoundException)`, owner-id command |
| `CoffeePeek.AccountService/Controllers/AdminUserPublicAddressesController.cs` | Admin-only backfill | VERIFIED | One action |
| `CoffeePeek.Account.Application/Features/User/GetProfile/GetPublicUserProfileHandler.cs` | D-04 body-only fix | VERIFIED | Condition only |
| `CoffeePeek.Gateway/appsettings.json` | by-slug route | VERIFIED | Additions only |
| Tests: `UserPublicAddressTests.cs` (87 lines), `UserPublicAddressPersistenceTests.cs` (137), `UserPublicAddressLifecycleTests.cs` (347, ≥200 required), `UserPublicAddressServiceTests.cs` (300, ≥150), `PublicUserAddressControllerTests.cs` (225, ≥100), `GetProfileHandlerTests.cs` (+31), `ReverseProxyRouteConfigurationTests.cs` (+64) | Regression locks | VERIFIED | All pass. Preconditions are asserted before each action, so the key tests are non-vacuous (the verifier read them all). |
| `docs/api/public-addresses.md` | Contract, policy, prerequisite, gate | VERIFIED | All 20 required strings are present. WR-01 wording must be corrected before enablement. |

### Key Link Verification

| From | To | Via | Status | Details |
|---|---|---|---|---|
| `AccountDbContext.SaveChangesAsync(bool, ct)` | `ProfileAddressWriter.SaveAsync` | Gate `IsRetiring \|\| (PublicAddressesEnabled && NeedsAddress)` inside `ProfileAddressTransaction.RunAsync` | WIRED | `UnitOfWork.SaveChangesAsync(ct)` and Wolverine's `EfCoreEnvelopeTransaction` both reach the overridden virtual overload |
| `ProfileAddressWriter` | `AccountDbContext.SaveCoreAsync` | Demotion flush plus final save | WIRED | No non-comment `SaveChangesAsync` in the writer; 4 mutation checks reported in 04-01-SUMMARY, and structure confirmed by reading |
| `Account.Persistence/DependencyInjection.cs` | `PublicAddressOptions` | `Configure<PublicAddressOptions>(GetSection("PublicAddresses"))` | WIRED | Production `AddDbContext` activates the context through DI, so `IOptions` is injected. Generated handlers use `GetRequiredService<AccountDbContext>`. |
| `DependencyInjection.cs` | `ProfileAddressService` | `AddScoped<IProfileAddressService, ProfileAddressService>()` | WIRED | Resolved by both controllers |
| `PublicUserAddressesController.GetBySlug` | `GetPublicUserProfileCommand` | `IMessageBus.InvokeAsync` with `address.EntityId`; NotFoundException becomes NotFound | WIRED | The test captures the command UserId == the resolved owner |
| `ProfileAddressService` | `db.Users` | Visibility filter on every read | WIRED | Resolve and batch; backfill uses `!IsSoftDelete` only (blocked included, by design) |
| Gateway `account-Users-by-slug-route` | `PublicUserAddressesController.GetBySlug` | account-cluster, auth-endpoints | WIRED (config and precedence API) | The runtime YARP smoke is a gate item |
| Docs "Deployment prerequisite" | `20260928134755_AddUserPublicAddresses` | Exact migration id | WIRED | Docs lines 9 and 33 |

### Data-Flow Trace (Level 4)

| Artifact | Data Variable | Source | Produces Real Data | Status |
|---|---|---|---|---|
| `PublicUserAddressesController.GetBySlug` | `address`, `response.Data` | `ProfileAddressService.ResolveAsync` (registry query plus `Users` visibility query), then `GetPublicUserProfileHandler`, `UserRepository.GetById` and Mapster | Yes | FLOWING |
| `GetBatch` / `GetById` | `PublicAddressMetadata[]` | `GetByIdsAsync`: a visible-id query and a canonical-row query, ordered by input | Yes | FLOWING |
| `AdminUserPublicAddressesController.Backfill` | `PublicAddressBackfillResult` | `Users` owner page, then per-owner canonical lookup and `FindCandidateAsync`; write mode inside `RunAsync` | Yes | FLOWING |
| SaveChanges hook | `users` snapshot | `ChangeTracker.Entries<User>()` after `DetectChanges` | Yes | FLOWING |

### Behavioral Spot-Checks (run by the verifier)

| Behavior | Command | Result | Status |
|---|---|---|---|
| Lifecycle, service, model, controller and codegen guard | `dotnet test CoffeePeek.Account.Infrastructure.Tests --no-build --filter "UserPublicAddress\|PublicUserAddressController\|WolverinePreGenerated"` | 36 passed, 0 failed (11 lifecycle, 7 model, 7 service, 10 controller, 1 codegen) | PASS |
| Full Account.Infrastructure (Debug and Release) | `dotnet test … --no-build` | 89/89 | PASS |
| Account.Domain (22-row username regression, lifecycle) | `dotnet test … -c Release --no-build` | 255/255 | PASS |
| Account.Application (D-04 handler facts) | `dotnet test … -c Release --no-build` | 216/216 | PASS |
| Gateway route shape and precedence | `dotnet test CoffeePeek.Gateway.Tests -c Release --no-build` | 56/56 | PASS |
| No model drift | `dotnet ef migrations has-pending-model-changes --no-build` (dummy connection string) | "No changes have been made to the model since the last migration." | PASS |
| Isolation / earlier migrations / whitespace | `git diff --quiet 96acf741 HEAD -- <Shops, Shared, Contract, Generated, UsersController, AdminUsersController, DTO, command, Mapper, Repositories, UserAggregate, ci-cd.yml, ci.slnf>`; earlier Account migrations; `git diff --check` | all exit 0 | PASS |
| Default-off | `grep -rnE 'PublicAddresses(__\|:\|")'` in AccountService/ShopsService appsettings, deploy/, AppHost; repo-wide `git grep` | no configuration anywhere | PASS |
| No cooldown, no audit | `grep UsernameChangedAtUtc`; `grep "class \w*Audit\|DbSet<\w*Audit"` over Account | none | PASS |

### Probe Execution

Step 7c: SKIPPED. No `scripts/*/tests/probe-*.sh` exist, and no phase PLAN or SUMMARY declares a probe.

### Requirements Coverage (P4 labels and CONTEXT decisions; no REQUIREMENTS.md)

| ID | Source Plan | Description | Status | Evidence |
|---|---|---|---|---|
| P4-01 | 04-01 | Canonical allocated at creation (email and Google) from the username, with deterministic suffixes | SATISFIED | Truth 1; pre-existing users are covered by backfill (truth 13) or the first enabled rename |
| P4-02 | 04-01 | Username → slug Policy A: aliases, A->B->A, same-slug no-op | SATISFIED | Truths 2, 3, 8 |
| P4-03 | 04-02 | Visible-only resolve on by-slug, batch and legacy GUID; identical 404; data read re-checks; no email/phone | SATISFIED | Truths 10, 11, 15 |
| P4-04 | 04-01 | Deletion retires all rows even when disabled; tombstones keep only the slug; no row keeps the user id | SATISFIED | Truths 4, 5; lifecycle test asserts `rows.Count(r => r.OwnerId == id) == 0` (see WR-01 on correlation) |
| P4-05 | 04-02 | Admin bounded backfill: dry-run, cursor, replay, deleted excluded, blocked included | SATISFIED | Truth 13 |
| P4-06 | 04-01, 04-02, 04-03 | GUID routes/DTOs unchanged except the D-04 fix; default-off; migration prerequisite; live gate documented; no codegen drift | SATISFIED | Truths 9, 11, 19, 21 |
| P4-07 | 04-03 | Gateway routes by-slug to Account for every slug, including `reviews` | SATISFIED | Truth 16 (runtime smoke is a gate item) |
| D-01 | 04-01, 04-03 | Slug follows the username; rename semantics | HONORED | Truths 1-3, 20 |
| D-02 | 04-03 | Squatting recorded as a gate item; no cooldown | HONORED | Truth 17 |
| D-03 | 04-01, 04-02, 04-03 | Public iff not deleted and not blocked; block hides without retiring; delete always retires; tombstones carry no private data | HONORED | Truths 4, 5, 10 |
| D-04 | 04-02, 04-03 | `GET api/Users/{id:guid}` returns 404 for deleted/blocked; body-only | HONORED | Truth 11 |
| D-05 | 04-01 | Account-local single kind; no Shops edits; parity model tests | HONORED | Truth 6 (WR-05 on test strength) |
| D-06 | 04-01, 04-02, 04-03 | No audit table, no history endpoint | HONORED | Truths 7, 14 |
| D-07 | 04-03 | Dedicated gateway by-slug route on auth-endpoints | HONORED | Truth 16 |
| D-08 | 04-02 | Canonical path `/users/{slug}` | HONORED | Truth 12 |
| D-09 | 04-03 | Crawlable Google usernames accepted and documented | HONORED | Truth 18 |
| D-10 | 04-02 | Admin scope is backfill only | HONORED | Truths 13, 14 |

There are no orphaned labels or decisions: all 17 are accounted for.

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|---|---|---|---|---|
| (all 24 phase files) | — | TBD/FIXME/XXX/TODO/HACK/PLACEHOLDER/NotImplemented | none found | — |
| `ProfileAddressService.cs` | 29 | `return null` | Info | Intended: a hidden or unknown slug returns null and the controller returns 404 |
| `docs/api/public-addresses.md` | 160 | Overstated privacy claim (WR-01) | Warning | Operator-facing doc is inaccurate about DB-level linkability; no API exposure |
| `UserPublicAddressPersistenceTests.cs` | 20-24 | Parity against pinned literals (WR-05) | Warning | Cannot detect future Shops drift; parity holds today |
| `UserPublicAddressLifecycleTests.cs` | 227 | `HardDelete_RetiresBeforeRemovingUser` cannot prove the "before" ordering on InMemory (IN-05) | Info | Retirement is proven; ordering under RESTRICT is a live-gate item |
| `PublicUserAddressControllerTests.cs` | 153-155 | Privacy probe checks only `Email`/`PhoneNumber` names (IN-05) | Info | The DTO is unchanged since base (diff exit 0), so there is no current risk |

### Review Findings: Blocking Assessment (04-REVIEW.md, 0 critical / 5 warnings)

- **WR-01: not blocking. The docs must be corrected before AccountService enablement.** The goal clause is "retire addresses *without copying private profile data into tombstones*". It holds in code:
  - A tombstone row has only the 7 base columns (model test).
  - `Retire` nulls `OwnerId` on every canonical and alias row, and no row keeps the user id (lifecycle test).
  - There is no audit table (D-06).
  - No email, display name, about or phone is copied anywhere.

  D-03 as decided ("Tombstones carry no private profile data") and D-06 ("no audit table and no admin history endpoint") are both met. No API returns tombstones: `ResolveAsync` returns null when `OwnerId` is null, and batch returns only canonical rows of visible users.

  The docs sentence at line 160, "nothing links a deleted user to former slugs", is nevertheless false at the database level. The verifier confirmed the review's evidence and found more:
  1. `RetireAllAsync` stamps every row with one shared `Revision` and one `DateTime.UtcNow` (ProfileAddressWriter.cs:81-84). `User` is an `IAuditableEntity` (via `Entity<TId>`), so `AuditInterceptor` sets `Users.UpdatedAtUtc` in the same save.
  2. `SetSoftDelete` only flips `IsSoftDelete`, so the retained `Users` row still holds `Username`, and the last canonical slug is recomputable from it.
  3. Suffixed and fallback slugs (`ivan-<8hex>`, `user-<8hex>`) embed the first 8 hex digits of the owner GUID (`PublicSlug.Candidate`), and user GUIDs are already public through Shops review DTOs.

  All three are residual correlation risks that need database access or an already-known old link. They come from the shared `PublicAddress`/`PublicSlug` design (identical in Shops) and the pre-existing soft-delete model, and research Pitfall 5 accepted the username-derived part as residual risk. **Current exposure is zero:** with `PublicAddresses__Enabled` unset, no address row is ever allocated, so no tombstone exists. Recommended fix: replace line 160 with the review's corrected wording, and also mention the GUID-derived suffix and the retained soft-deleted `Users` row. If the stronger unlinkability guarantee is wanted, record it as an enablement-gate decision. It would require a shared `PublicAddress` change applied to Shops too.
- **WR-02: not blocking.** Confirmed in the generated `AdminDeleteUserCommandHandler`: an `EfCoreEnvelopeTransaction` wraps the handler, so `pg_advisory_xact_lock` is held until `CommitAsync`, after the Redis/SignalR calls. Correctness is unaffected. With the flag off, only deletions take the lock, and they are rare. Add a lock-hold/Redis-latency item to the AccountService enablement gate, or move the external I/O to a cascaded post-commit message, which needs a handler change and codegen.
- **WR-03: not blocking.** D-07 locked "the existing `auth-endpoints` rate limit". The existing GUID profile route already shared this bucket before the phase. Recommend adding to docs line 113 that login, refresh and registration share the bucket, and revisiting this in phase 6.
- **WR-04: not blocking.** It is latent while disabled, because `RetireAllAsync` returns before `Retire` when there are no rows. Clamp `now` and add the seeded-future-timestamp test before enablement.
- **WR-05: not blocking.** The verifier checked parity directly: the two `Configure` bodies are identical today. Renaming the test or sharing the SQL constants is a follow-up.
- Info IN-01..IN-07 are latent, pre-existing or documentation nits. None falsifies a must-have. IN-03, the stale full-row `Update` reviving a concurrently deleted user, predates this phase.

### Human Verification Required

None for phase completion. The manual-only items in 04-VALIDATION.md are two:
- the live PostgreSQL behaviors;
- real YARP routing of `/api/Users/by-slug/reviews`.

The phase goal defines both as the enablement boundary ("feature default-off behind the live PostgreSQL gate"). Both are documented as pending in `docs/api/public-addresses.md` (steps 2 and 5-6) and recorded under `infrastructure_gap` in the frontmatter, following the phase 1–3 precedent.

### Gaps Summary

There are no goal gaps. The codebase delivers what the phase set out to build:
- An Account-local, drift-free `UserPublicAddresses` registry with Shops-identical schema and a RESTRICT FK to Users.
- A codegen-free SaveChanges hook. It allocates at email and Google creation, follows username changes (aliases, A->B->A, same-slug no-op, owner-aware suffixes, `user-<8hex>` fallback), and always retires on soft or hard deletion, even with the flag off. Its tombstones carry no private data.
- A visibility rule (not deleted, not blocked) enforced on by-slug, batch, legacy GUID and the pre-existing GUID profile route, which is now fixed per D-04.
- Anonymous NoStore endpoints with identical 404s.
- An Admin-only bounded backfill.
- A dedicated gateway route that keeps the `reviews` slug out of Shops.
- A runbook with the flag-independent migration prerequisite and the pending Account live gate.

The feature stays unconfigured and default-off, and no Shops, Shared, Contract, DTO or generated-handler file changed.

Open before AccountService enablement (not phase gaps):
- the live PostgreSQL and HTTP gate items;
- the D-02 squatting decision;
- correcting the WR-01 privacy sentence in the docs;
- the WR-02 lock-hold and WR-04 clock-clamp follow-ups.

---

_Verified: 2026-09-29T07:46:59Z_
_Verifier: Claude (gsd-verifier)_
