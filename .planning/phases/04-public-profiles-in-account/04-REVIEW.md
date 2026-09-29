---
phase: 04-public-profiles-in-account
reviewed: 2026-09-29T07:38:02Z
depth: standard
files_reviewed: 22
files_reviewed_list:
  - CoffeePeek.Account.Application.Tests/Features/User/GetProfile/GetProfileHandlerTests.cs
  - CoffeePeek.Account.Application/Common/Interfaces/IProfileAddressService.cs
  - CoffeePeek.Account.Application/Common/Options/PublicAddressOptions.cs
  - CoffeePeek.Account.Application/Features/User/GetProfile/GetPublicUserProfileHandler.cs
  - CoffeePeek.Account.Domain.Tests/UserPublicAddressTests.cs
  - CoffeePeek.Account.Domain/Entities/PublicAddressAggregate/UserPublicAddress.cs
  - CoffeePeek.Account.Infrastructure.Tests/Persistence/UserPublicAddressLifecycleTests.cs
  - CoffeePeek.Account.Infrastructure.Tests/Persistence/UserPublicAddressPersistenceTests.cs
  - CoffeePeek.Account.Infrastructure.Tests/Persistence/UserPublicAddressServiceTests.cs
  - CoffeePeek.Account.Infrastructure.Tests/PublicUserAddressControllerTests.cs
  - CoffeePeek.Account.Persistence/Configuration/AccountDbContext.cs
  - CoffeePeek.Account.Persistence/Configuration/UserPublicAddressConfiguration.cs
  - CoffeePeek.Account.Persistence/DependencyInjection.cs
  - CoffeePeek.Account.Persistence/Migrations/20260928134755_AddUserPublicAddresses.cs
  - CoffeePeek.Account.Persistence/PublicAddresses/ProfileAddressService.cs
  - CoffeePeek.Account.Persistence/PublicAddresses/ProfileAddressTransaction.cs
  - CoffeePeek.Account.Persistence/PublicAddresses/ProfileAddressWriter.cs
  - CoffeePeek.AccountService/Controllers/AdminUserPublicAddressesController.cs
  - CoffeePeek.AccountService/Controllers/PublicUserAddressesController.cs
  - CoffeePeek.Gateway.Tests/ReverseProxyRouteConfigurationTests.cs
  - CoffeePeek.Gateway/appsettings.json
  - docs/api/public-addresses.md
findings:
  critical: 0
  warning: 5
  info: 7
  total: 12
status: issues_found
---

# Phase 4: Code Review Report

**Reviewed:** 2026-09-29T07:38:02Z
**Depth:** standard
**Files Reviewed:** 22
**Status:** issues_found

## Summary

I reviewed the phase 4 diff (`96acf741..HEAD`, including the two handler tests in docs commit `21419ba3`) against locked decisions D-01..D-10. I also traced the code it calls: the Wolverine-generated handlers, `UnitOfWork`, `UserRepository`, `AuditInterceptor`, the deletion and rename handlers, the gateway rate limiter and `PublicAddress`/`PublicSlug`. The 35 new Account infrastructure tests, 19 gateway route tests and 4 profile handler tests pass locally on InMemory.

I found no blocker. These parts are correct:
- **Visibility.** It is enforced on all four read paths (by-slug, batch, legacy GUID, GUID profile). The by-slug data read re-checks visibility, and the public DTO has no email or phone.
- **Hook change detection.** Username changes are detected by comparing original and current values, never with `IsModified`. The writer saves only through `SaveCoreAsync`, so the hook is never re-entered. Retirement runs on soft and hard delete even when the flag is off. A→B→A restores the old slug, and a suffixed owner's case-only rename is a no-op.
- **Transaction helper.** It matches the Shops `PublicAddressTransaction`: a savepoint inside Wolverine's ambient transaction, and an advisory lock held until the outer transaction commits.
- **Gateway precedence.** `/api/Users/by-slug/{slug}` really outranks `/api/users/{userId}/reviews`. The test proves this with `RoutePrecedence.ComputeInbound`, not only with JSON order.
- **Admin backfill.** Admin is enforced both at the gateway and in AccountService.
- **Migration.** It contains only the registry table, 3 indexes, the FK and 4 checks. `Down` is the plain drop, and the docs say not to run it after allocation.

The warnings cover four things:
- **Privacy claim.** The docs say tombstones cannot be linked to the deleted user. They can: the shared retirement timestamp and revision link them.
- **Global lock and rate limit.** A single global advisory lock is held across Redis/SignalR calls, even with the flag off. The `auth-endpoints` bucket is shared with login and token refresh.
- **Clock dependency.** Deletion needs a wall clock that never moves backwards.
- **Parity tests.** The "Shops parity" schema test cannot detect Shops drift.

## Warnings

### WR-01: Tombstones stay linkable to the deleted user and to each other; the docs claim otherwise

**File:** `CoffeePeek.Account.Persistence/PublicAddresses/ProfileAddressWriter.cs:81-84`, `docs/api/public-addresses.md:160`
**Issue:** `RetireAllAsync` gives every row of the owner the same `Revision` (`max + 1`) and the same `UpdatedAtUtc` (one `DateTime.UtcNow`). The soft-deleted `Users` row is never purged: it keeps `Username`, `Email` and `UpdatedAtUtc`. `AuditInterceptor` sets that `UpdatedAtUtc` in the same save, milliseconds after the tombstone timestamp. The first canonical's `CreatedAtUtc` likewise sits within milliseconds of the user's `CreatedAtUtc`.

Anyone with database read access can therefore do two things:
- Group tombstones by `(UpdatedAtUtc, Revision)` to rebuild a deleted user's complete slug history, including the former usernames that the aliases encode.
- Join that group to the user by timestamp.

The docs say: "Account keeps no address audit or history, so nothing links a deleted user to former slugs". That is false. D-06 and Pitfall 5 existed to prevent exactly this link. No API exposes tombstones, so this is not an external leak. The problem is a wrong privacy statement and a weaker D-03 guarantee.

**Fix:** At minimum, correct the docs:
```markdown
- Account keeps no address audit table. Tombstones have no owner column, but every row of a deleted user is retired
  with one shared revision and timestamp, which are close to the retained soft-deleted `Users` row's timestamps.
  With database access, a user's former slugs can therefore be grouped and correlated with the deleted account.
  The API never exposes tombstones.
```
If the stronger guarantee is required, record it as an enablement-gate item. It would mean coarsening tombstone timestamps (for example to the day) and not sharing a single revision across rows. That is a change to the shared `PublicAddress` and needs the same treatment in Shops.

### WR-02: The global advisory lock is held across Redis/SignalR calls, and every deletion takes it even when the flag is off

**File:** `CoffeePeek.Account.Persistence/PublicAddresses/ProfileAddressTransaction.cs:12-24`, `CoffeePeek.Account.Persistence/Configuration/AccountDbContext.cs:30-36` (callers: `ConfirmAccountDeletionHandler.cs:73-79`, `AdminDeleteUserCommand.cs:38-40`)
**Issue:** Inside Wolverine's ambient transaction, `pg_advisory_xact_lock(7402100401)` is released only when the outer transaction commits (`efCoreEnvelopeTransaction.CommitAsync`, in the generated handlers).

Both deletion handlers call `revokedUserStore.RevokeAccessTokensAsync` (Redis) and `sessionTerminationNotifier.NotifyForceLogoutAsync` (SignalR) after `unitOfWork.SaveChangesAsync`, and before the middleware commits. The single Account-wide lock is therefore held for the length of those network calls:
- **Flag off (production today).** Retirement always takes the lock, so account deletions now serialize behind each other's Redis/SignalR latency. This is a new behavior of this build.
- **Flag on.** Every registration, Google sign-up, username change and write-mode backfill also queues behind any slow deletion. A Redis stall (client timeouts of several seconds) blocks sign-ups service-wide.

**Fix:** Run the external I/O after the commit, so the lock covers only the database work. For example, cascade an internal message that Wolverine delivers after commit:
```csharp
// AdminDeleteUserHandler / ConfirmAccountDeletionHandler: return a cascading message instead of calling out inline
public record UserSessionsTerminationRequested(Guid UserId, string Reason);
// ...
await userRepository.Update(user, ct);
await unitOfWork.SaveChangesAsync(ct);
return (Response<bool>.Success(true), new UserSessionsTerminationRequested(user.Id, SessionTerminationReasons.UserDeleted));

// separate handler, runs after the outbox commit
public static class UserSessionsTerminationRequestedHandler
{
    public static async Task Handle(UserSessionsTerminationRequested msg, IRevokedUserStore store,
        ISessionTerminationNotifier notifier, IOptions<JWTOptions> jwt, CancellationToken ct)
    {
        await store.RevokeAccessTokensAsync(msg.UserId, jwt.Value, ct);
        await notifier.NotifyForceLogoutAsync(msg.UserId, msg.Reason, ct);
    }
}
```
This changes handler signatures, so codegen must be rerun. If that is out of scope, add this to the enablement gate with the lock-hold analysis and a Redis-latency live test.

### WR-03: The `auth-endpoints` bucket is shared with login and token refresh, so profile browsing can 429 a session refresh

**File:** `CoffeePeek.Gateway/appsettings.json:77-86` (with `CoffeePeek.Gateway/Extensions/RateLimitingExtensions.cs:25,63-71`), `docs/api/public-addresses.md:113`
**Issue:** `AddPolicy("auth-endpoints", ctx => PerClient(...))` creates one partitioned limiter per policy, keyed only by client IP. Every route with `RateLimiterPolicy: auth-endpoints` draws from the same 20-per-minute bucket per IP:
- `account-Tokens-route`: login, Google login, native refresh `PUT api/Tokens/native`
- the whole `/api/Users/**` catch-all: registration, password reset, email confirmation
- the new by-slug route
- the batch and legacy-GUID lookups

The intended frontend makes one batch call per review page plus by-slug profile views. That uses up the bucket, and the next token refresh gets 429, which logs the user out. Clients behind one NAT or an SSR server share the bucket too. D-07 locks the limit value. The docs present it as "20/min per client IP" for the three lookups, but they do not say the bucket is shared with authentication. The "revisit in phase 6" note therefore underestimates the impact.

**Fix:** Keep the D-07 numbers but isolate the bucket. This needs a D-07 amendment:
```csharp
public const string PublicProfilePolicy = "public-profile-reads";
options.AddPolicy(PublicProfilePolicy, ctx => PerClient(ctx, TimeSpan.FromMinutes(1), 6, 20));
```
```json
"account-Users-by-slug-route": { "ClusterId": "account-cluster",
  "Match": { "Path": "/api/Users/by-slug/{slug}" }, "RateLimiterPolicy": "public-profile-reads" }
```
Give batch and legacy-GUID dedicated routes on the same policy too. If D-07 stays as it is, state the shared-bucket consequence in `docs/api/public-addresses.md:113` and in enablement-gate step 5/6.

### WR-04: Account deletion (flag on or off) and username renames throw 500 if the wall clock moves backwards

**File:** `CoffeePeek.Account.Persistence/PublicAddresses/ProfileAddressWriter.cs:82-84,110-116` (enforced by `CoffeePeek.Shared.Domain/PublicAddresses/PublicAddress.cs:45`)
**Issue:** `Retire`, `MakeAlias` and `Restore` throw `ArgumentOutOfRangeException("Time cannot move backwards")` when `DateTime.UtcNow` is earlier than a row's stored `UpdatedAtUtc`. This can happen after an NTP step-back, or when rows were written by a host whose clock ran ahead.
- The exception is not a `BaseException`, so `GlobalExceptionHandler` returns 500.
- Wolverine's `TransientDbConflict` policy does not retry it.
- The user cannot delete their account until the clock passes the stored timestamp.

Retirement runs even with the flag off, so a privacy-critical path now depends on a monotonic wall clock. It is rare on a single VPS, but the failure mode is a stuck deletion.

**Fix:** Clamp `now` to the newest timestamp among the rows being transitioned:
```csharp
// RetireAllAsync
var now = rows.Max(a => a.UpdatedAtUtc) is var latest && latest > DateTime.UtcNow ? latest : DateTime.UtcNow;

// FollowUsernameAsync
var now = new[] { DateTime.UtcNow, current.UpdatedAtUtc, row?.UpdatedAtUtc ?? DateTime.MinValue }.Max();
```
Add a lifecycle test that seeds a row with `UpdatedAtUtc = DateTime.UtcNow.AddMinutes(5)` and then soft-deletes the owner.

### WR-05: "Parity with the Shops registries" (D-05) is checked against literal copies, so Shops drift goes unnoticed

**File:** `CoffeePeek.Account.Infrastructure.Tests/Persistence/UserPublicAddressPersistenceTests.cs:20-24,103-115`
**Issue:** D-05 requires "schema model tests pinning parity with the Shops registries". The test `Model_CheckConstraintsMatchShopsRegistriesExactly` compares the Account model with string constants copied into the test ("Literal copies ... do not reference Shops"). If a later phase changes a Shops check constraint, filter, collation or max length, this test still passes and the two registries diverge silently. The test name promises something the test cannot detect, so it gives false assurance. The index and collation tests have the same limitation.

**Fix:** Keep the SQL fragments in one place and use them from both configurations and from the test. For example, add a `PublicAddressSchema` static class in `CoffeePeek.Shared.Domain` or `CoffeePeek.Shared.Persistence`:
```csharp
public static class PublicAddressSchema
{
    public const string SlugCheck = "\"Slug\" ~ '^[a-z0-9]+(-[a-z0-9]+)*$' AND length(\"Slug\") BETWEEN 1 AND 100";
    public const string LifecycleCheck = "(\"State\" IN (1, 2) AND \"OwnerId\" IS NOT NULL) OR (\"State\" = 3 AND \"OwnerId\" IS NULL)";
    public const string CanonicalOwnerFilter = "\"State\" = 1";
    // ...
}
```
Otherwise, rename the test to `Model_CheckConstraintsMatchPinnedLiterals` and add a cross-service test that loads both design-time models and compares them.

## Info

### IN-01: The demotion flush ignores the caller's `acceptAllChangesOnSuccess`

**File:** `CoffeePeek.Account.Persistence/PublicAddresses/ProfileAddressWriter.cs:114`
**Issue:** `SaveCoreAsync(true, ct)` always accepts changes, even when the caller asked for `SaveChanges(false)` (the EF retry pattern). The User row and every other pending change become `Unchanged`, so if the caller retries after a failed final save, those changes are silently not written. No current caller uses `false`.
**Fix:** Pass `acceptAllChangesOnSuccess` through and call `ChangeTracker.AcceptAllChanges()` only at the end, or document that the hook does not support `SaveChanges(false)`.

### IN-02: Username and soft-delete detection is blind to detached entities attached with `DbSet.Update`

**File:** `CoffeePeek.Account.Persistence/PublicAddresses/ProfileAddressWriter.cs:20-29`
**Issue:** When an untracked `User` is passed to `UserRepository.Update`, EF sets `OriginalValues` equal to the current values. `IsRetiring` and `NeedsAddress` then both return false, so a soft delete through that path would not retire the addresses, which breaks the always-retire privacy rule. Every current call site loads the user with tracking (`GetById`/`GetByProvider`), so this is latent.
**Fix:** Add a guard comment on `IUserRepository.Update`. Alternatively, when a Modified `User` entry has `OriginalValues` equal to its current values for `Username`/`IsSoftDelete`, read the database values (`entry.GetDatabaseValuesAsync`) before classifying it.

### IN-03: A rename racing a deletion revives the user (pre-existing) and then gets a fresh canonical address

**File:** `CoffeePeek.Account.Persistence/Repositories/UserRepository.cs:25-29` (root cause), `CoffeePeek.Account.Persistence/PublicAddresses/ProfileAddressWriter.cs:97-103`
**Issue:** The sequence is:
1. A rename request loads user U (`IsSoftDelete=false`).
2. A deletion commits and retires all of U's rows.
3. The rename's hook takes the lock, finds no canonical, and calls `InitializeAsync`.
4. `DbSet.Update` writes the whole stale row, including `IsSoftDelete=false` and un-revoked refresh tokens.

The deleted user comes back as visible with a new slug. The full-row lost update existed before this phase; the phase only adds the allocation on top.
**Fix:** Outside this phase: add a concurrency token to `User`. Inside the hook: while holding the lock, re-read `IsSoftDelete` from the database for renamed owners and skip allocation if the owner is now deleted.

### IN-04: Legacy GUID endpoint returns 400 for `Guid.Empty`, and the OpenAPI error type is wrong

**File:** `CoffeePeek.AccountService/Controllers/PublicUserAddressesController.cs:24,69-77`
**Issue:** `GET api/Users/00000000-.../public-address` reaches `GetByIdsAsync([Guid.Empty])` and gets a 400 `ValidationException`. Neither the docs nor `ProducesResponseType` list a 400; the docs describe 404 for unknown users. Separately, `[ProducesErrorResponseType(typeof(ErrorResponse))]` claims every error body is an `ErrorResponse`, but 404 returns `ProblemDetails` and invalid `ids` return `ValidationProblemDetails`.
**Fix:** Return `NotFound()` when `id == Guid.Empty` (or document the 400). Declare `ProducesResponseType<ProblemDetails>(404)` on the three actions.

### IN-05: Weak or partly vacuous test assertions

**File:** `CoffeePeek.Gateway.Tests/ReverseProxyRouteConfigurationTests.cs:178-181`, `CoffeePeek.Account.Infrastructure.Tests/Persistence/UserPublicAddressLifecycleTests.cs:227-248`, `CoffeePeek.Account.Infrastructure.Tests/PublicUserAddressControllerTests.cs:153-155`
**Issue:** Three assertions check less than they appear to:
- `IndexOf(by-slug) < IndexOf(catch-all)` checks JSON declaration order, which YARP ignores. Only the precedence test that follows proves routing.
- `HardDelete_RetiresBeforeRemovingUser` runs on InMemory, which has no FK. It proves that retirement happens, but not the "Before" in its name, which is statement ordering under the RESTRICT FK.
- The privacy check only probes the property names `Email` and `PhoneNumber`, so a future `EmailAddress` or `Credentials` property would pass.

**Fix:**
- Drop or relabel the order assertion.
- Rename the hard-delete test (for example `HardDelete_RetiresAllAddresses`) and keep the ordering as a live-gate item.
- Assert the exact property set of `PublicUserProfileResponse`.

### IN-06: A rollback failure hides the original exception

**File:** `CoffeePeek.Account.Persistence/PublicAddresses/ProfileAddressTransaction.cs:27-40`
**Issue:** If `RollbackAsync` or `RollbackToSavepointAsync` throws (for example on a broken connection), that exception propagates from the `catch` block and replaces the original. The `DbUpdateException` → 409 mappings in `UnitOfWork` and `ProfileAddressService.IsAddressConflict` are then bypassed, and the root cause disappears from the logs. The Shops copy has the same behavior.
**Fix:** Catch the rollback exception, log it, and rethrow the original:
```csharp
catch (Exception original)
{
    try { /* rollback */ }
    catch (Exception rollback) { throw new AggregateException(original, rollback); }
    finally { db.ChangeTracker.Clear(); }
    throw;
}
```

### IN-07: Two behaviors are missing or incomplete in the docs

**File:** `CoffeePeek.Account.Persistence/PublicAddresses/ProfileAddressWriter.cs:34-35`, `CoffeePeek.Account.Persistence/PublicAddresses/ProfileAddressService.cs:78-80`, `docs/api/public-addresses.md:144-146`
**Issue:**
- `Stem` also maps a legitimate username `Place` or `place` to `user-<8hex>`, because `Generate` returns "place" for it. The docs list only reserved, `user` and unsupported-script names.
- A username change made while the flag is off leaves the canonical slug on the old name. The docs mention this, but not that there is no repair tool: backfill replay "never renames", and users have no admin rename (D-10). The only fix is another username change by the user. Backfill will also report such users with `hadCollision=true`, which is misleading.

**Fix:** Add `place` to the docs example list. Also state that renames made while the flag is off can only be repaired by the user renaming again, and that `hadCollision` is then misleading.

---

_Reviewed: 2026-09-29T07:38:02Z_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
