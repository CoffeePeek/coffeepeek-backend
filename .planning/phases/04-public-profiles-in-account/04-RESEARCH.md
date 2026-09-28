# Phase 4: Public profiles in Account - Research

**Researched:** 2026-09-28
**Domain:** Extending the phase 1–3 public-address model (shared `PublicSlug`/`PublicAddress`, EF Core 10 + PostgreSQL, ASP.NET Core controllers, YARP) into the separate Account bounded context and database (`cpaccountdb`)
**Confidence:** HIGH for codebase facts (file:line verified; three throwaway spikes run and deleted). MEDIUM for the product-policy recommendations: they are user decisions, listed under Open Questions.

## User Constraints

> No `04-CONTEXT.md` exists yet. The constraints below come from `.planning/ROADMAP.md` (Phase 4), the approved spec `.planning/specs/PUBLIC-SLUGS.md` and the orchestrator brief. Treat them as locked. The user decisions under Open Questions must be answered (CONTEXT.md) before planning.

### Locked Decisions
- Reuse the shared slug rules `CoffeePeek.Shared.Domain/PublicAddresses/PublicSlug.cs` and the phase 1–3 model: typed registry, lifecycle, lookup, rename/history, bounded backfill. The work stays **inside the Account bounded context**: `CoffeePeek.Account.*` / `CoffeePeek.AccountService`, a separate DB, migrations via `make mig-acc n=...`.
- An explicit profile privacy rule. Private or deleted profiles never resolve or leak.
- An explicit username → slug policy.
- Account deletion retires addresses **without copying private profile data into tombstones** (PUBLIC-SLUGS.md:15).
- Existing GUID routes and DTOs stay unchanged. `CoffeePeek.Contract` changes must be additive only.
- The feature is default-off behind the live PostgreSQL gate (`PublicAddresses__Enabled`).
- Spec rules that apply: "Slug namespaces are per entity type, including canonical addresses, aliases and permanently reserved tombstones" and "A→B→A is supported for the same entity, while another entity cannot claim A" (PUBLIC-SLUGS.md:11). "Tombstones remove the owner reference but retain the reserved slug" (:15). "Visibility checks must match existing public entity rules. Tombstones/unknown/private resources must not leak private information" (:23). "Redirect targets must be server-derived paths" (:25). "Generation rules must not retroactively rewrite published addresses" (:9).

### Claude's Discretion
- File split, service shape, lock key, fallback stem for unsupported usernames, test layout.

### Deferred Ideas (OUT OF SCOPE)
- External frontend routing, 301s, canonical tags, sitemaps (phase 6).
- Operational/production backfill and enablement (phase 5).
- Cross-service account cleanup and physical deletion. The deletion slice deferred these (STATE.md:83-85).

## Project Constraints (from CLAUDE.md)

- .NET 10 / C# 14. No stack changes. Central package management. **This phase needs no new packages.**
- Layered DDD: Domain → Application → Infrastructure/Persistence. Controllers stay thin.
- Wolverine handlers are static/instance `Handle` methods discovered by scanning. Production uses `TypeLoadMode.Static` (`CoffeePeek.Shared.Persistence/Extensions/WolverineModule.cs:44-46`). **Any new handler, or any handler signature or dependency change**, requires `dotnet run -- codegen write` in `CoffeePeek.AccountService`, with the output committed under `Internal/Generated/WolverineHandlers/`. `CoffeePeek.Account.Infrastructure.Tests/WolverinePreGeneratedHandlersTests.cs` guards this. The recommended design changes **no handler signatures and adds no handlers**.
- The CLAUDE.md stack table says Wolverine 5.30, but `Directory.Packages.props:28` pins **WolverineFx 6.35.0** [VERIFIED: codebase]. This is doc drift only.
- `CoffeePeek.Contract` DTOs are additive only. The recommended design reuses `PublicAddressMetadata`/`PublicAddressEnvelope<T>`/`PublicAddressBackfillResult` (`CoffeePeek.Contract/Dtos/PublicAddresses/PublicAddressDtos.cs:3-8`) unchanged and adds nothing to Contract.
- Do not rename the `CoffePeek.*`, `Shops.Persistance` or `CoffeeShop.Moderation.*` typos.
- Schema changes go only through EF migrations via the Makefile (`make mig-acc n=...`).
- Conventions: file-scoped namespaces, primary constructors, `record` DTOs, `_camelCase` fields, `ct` in handlers, XML docs on public controller actions.
- Tests use xUnit v3, FluentAssertions and Moq, with `TestContext.Current.CancellationToken`. Core value: every fixed behavior gets a regression test.
- GSD workflow: edits happen only through `/gsd-execute-phase`.

## Summary

Account **already has a public profile endpoint**. `GET api/Users/{id:guid}` (`UsersController.cs:36-45`) has no `[Authorize]`, and the gateway route `account-Users-route` (`CoffeePeek.Gateway/appsettings.json:77-81`) has no `AuthorizationPolicy`, with no fallback policy in either place. So it is anonymous, limited only by the `auth-endpoints` 20/min per-IP limiter. It returns `PublicUserProfileResponse` (`UserName, CreatedAtUtc, About, AvatarUrl, ReviewCount, CheckInCount, AddedShopsCount`, `PublicUserProfileResponse.cs:3-10`), with no email or phone.

**The endpoint leaks today.** `GetPublicUserProfileHandler` loads the user and maps it without checking `IsSoftDelete` or `IsBlocked` (`GetPublicUserProfileHandler.cs:16-25`). Deleted and blocked users' profiles remain public by GUID. No privacy or visibility flag exists anywhere in Account (grep: no `Visibility|IsPublic|IsPrivate`). "Deletion" is only a soft delete: `IsSoftDelete = true` on confirmation (`ConfirmAccountDeletionHandler.cs:68-73`) or admin delete (`AdminDeleteUserCommand.cs:34-38`). The user row, including username, email and about, stays. No code physically deletes a user, and `AccountDeletionRequestedV1` has no consumer.

**Usernames cannot be slugs 1:1.** `Username` allows any Unicode letter, digits, `.` and `_`, keeps case, and is 3–30 chars (`Username.cs:8-9,41`). It has a case-sensitive unique index (`UserConfiguration.cs:16-21`; snapshot `AccountDbContextModelSnapshot.cs:249-259`, no collation). `PublicSlug` accepts only `[a-z0-9]+(-[a-z0-9]+)*` (`PublicSlug.cs:75`). The spike confirmed that distinct usernames collapse to one slug: `Ivan`/`ivan`/`Иван` → `ivan`, and `ivan.petrov`/`Ivan_Petrov`/`ivan__petrov` → `ivan-petrov`. Reserved or unsupported names fall back to `place` (`admin`, `settings`, `null`, CJK, Greek, Georgian, Arabic).

So the slug must be **derived from the username and reserved in a typed registry**, exactly like shops. Users can change their username at any time, with no cooldown (`UpdateProfileUsernameHandler.cs:17-24`). The username → slug policy is therefore the central product decision (Open Question 1).

**The Shops coordinator cannot be referenced from Account.** `PublicAddressTransaction`, `PublicAddressWriter` and the `Configure<T>` helper are `internal static` and typed to `ShopsDbContext` (`PublicAddressTransaction.cs:8`, `PublicAddressWriter.cs:12`, `PublicAddressConfiguration.cs:47-49`). The kind enum, options and exception live in `CoffeePeek.Shops.Application` (`IPublicAddressService.cs:7-16`). What is already shared is exactly what Account needs unchanged: `PublicSlug`, the `PublicAddress` base, `PublicAddressState` (Shared.Domain) and the Contract DTOs.

**Primary recommendation:** Add a lean, **single-kind, Account-local** registry:
- `UserPublicAddress : PublicAddress`, a `UserPublicAddresses` table with a RESTRICT FK to `Users`.
- A SaveChanges hook in `AccountDbContext` that allocates on user creation, follows username changes, and always retires on soft or hard deletion.
- A visibility rule of `!IsSoftDelete && !IsBlocked`.
- Public by-slug, batch and legacy-GUID lookups under `api/Users`, plus admin backfill under `api/admin/users/public-addresses`.
- No audit table, so no owner linkage survives deletion.
- One gateway route, so the slug `reviews` is not captured by Shops.

This is two plans. No Wolverine handler changes, no Contract changes, no Shops changes.

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Slug generation/validation | Shared Domain (`PublicSlug`, unchanged) | Account Persistence (fallback stem only) | Rules are versioned and shared; Account only chooses the stem |
| Address row lifecycle (canonical/alias/tombstone) | Shared Domain (`PublicAddress` base) + Account Domain (`UserPublicAddress`) | — | Same base class and transitions as shops |
| Uniqueness, at-most-one canonical, RESTRICT FK, checks | Database (`cpaccountdb`) | Account Persistence config | The DB is the uniqueness authority (spec) |
| Allocate on create, follow username, retire on delete | Account Persistence (`AccountDbContext.SaveChangesAsync` hook) | — | Generated Wolverine handlers `new` repositories directly (for example `RegisterUserCommandHandler748332044.cs`), so the DbContext is the only codegen-free interception point |
| Profile visibility (not deleted, not blocked) | Account Persistence (address service) + Application (`GetPublicUserProfileHandler` re-check) | — | Live DB read, never cached |
| Public lookup (by-slug, batch, legacy GUID) | API (AccountService controller, `api/Users`) | Gateway (existing `/api/Users/**` + one new by-slug route) | Anonymous and rate-limited |
| Admin backfill | API (`api/admin/users/public-addresses`) | Gateway (`account-admin-route`, Admin) | Admin policy enforced at both layers |
| Author links in shop reviews/feeds | External frontend via batch endpoint | Shops (unchanged, `ReviewDto.UserId`) | No cross-service denormalization |
| Page routes, 301s, canonical tags | External frontend | — | Phase 6 |

## Standard Stack

No new libraries. Everything is already in the repo [VERIFIED: `Directory.Packages.props`].

| Library | Version | Purpose | Note |
|---------|---------|---------|------|
| Microsoft.EntityFrameworkCore | 10.0.5 | Model, change-tracking hook | `Directory.Packages.props:8` |
| Npgsql.EntityFrameworkCore.PostgreSQL | 10.0.1 | Registry table, advisory lock, unique-violation detection | `:13` |
| Aspire.Npgsql.EntityFrameworkCore.PostgreSQL | 13.2.2 | Dev registration (pooled DbContext) | `:6`. Pooling verified to work with an optional `IOptions<T>` ctor parameter (see Pitfall 7) |
| Microsoft.EntityFrameworkCore.InMemory | 10.0.5 | Behavior tests (not constraint proof) | `:66`, already referenced by `CoffeePeek.Account.Infrastructure.Tests.csproj` |
| WolverineFx | 6.35.0 | Unchanged; existing `GetPublicUserProfileCommand` reused via `IMessageBus` | `:28` |
| dotnet-ef | 10.0.5 | Migration generation | `dotnet ef --version` → 10.0.5 [VERIFIED: local] |
| xUnit v3 3.2.2 / FluentAssertions 8.9.0 / Moq 4.20.72 | — | Tests | existing |

**Installation:** none.

## Package Legitimacy Audit

This phase installs no external packages, so slopcheck was not run. **Packages removed:** none. **Packages flagged:** none.

<phase_requirements>
## Phase Requirements

No REQUIREMENTS.md IDs exist for this phase. The planner should use these research labels (P4-xx, not official IDs):

| Label | Behavior | Research Support |
|-------|----------|------------------|
| P4-01 | Every non-deleted user gets one readable canonical address, allocated when the user is created (email registration and Google OAuth), derived from the username via `PublicSlug`, with deterministic suffixes on collision | Q2/Q4 evidence, Pattern 2 |
| P4-02 | Username → slug policy per Open Question 1. Recommended: a username change is an owner-authorized rename; old slugs become aliases; A→B→A restores; case- or punctuation-only changes are no-ops | Q2 evidence, Pattern 3 |
| P4-03 | Privacy: by-slug, batch and `{id}/public-address` resolve only visible users (`!IsSoftDelete && !IsBlocked`); unknown, deleted, blocked and tombstoned slugs return the identical 404; the data read re-checks visibility; the payload is the existing `PublicUserProfileResponse` (no email or phone) | Q1/Q5 evidence, Pattern 4 |
| P4-04 | Deletion (confirmation, admin delete, any future hard delete) retires **all** of the user's addresses even when the feature is disabled. Tombstones keep only the slug; no row anywhere keeps the user id | Q4 evidence, Pattern 2, Pitfall 5 |
| P4-05 | Admin bounded backfill: dry-run, cursor, idempotent replay, non-deleted users only (blocked users included but hidden) | Pattern 5 |
| P4-06 | Existing GUID routes and DTOs unchanged (except the Open Question 3 privacy fix); default-off; the migration is a deployment prerequisite; the live gate is documented; no Wolverine codegen drift | Pitfalls, Validation |
| P4-07 | The gateway routes `api/Users/by-slug/{slug}` to Account for every valid slug, including `reviews` | Q5 evidence, Pitfall 4 |
</phase_requirements>

## Evidence: current public profile surface (Q1)

| Item | Evidence | Public? |
|---|---|---|
| `GET api/Users/{id:guid}` → `GetPublicUserProfileCommand` | `UsersController.cs:36-45`: no `[Authorize]`. Gateway `account-Users-route` `/api/Users/{**remainder}` has no policy (`appsettings.json:77-81`). No `FallbackPolicy` in the gateway (`AuthExtensions.cs:111-118`) or AccountService (`DependencyInjection.cs:28-31`) | **Anonymous**, 20/min per IP (`RateLimitingExtensions.cs:25`) |
| Payload | `PublicUserProfileResponse(UserName, CreatedAtUtc, About, AvatarUrl, ReviewCount, CheckInCount, AddedShopsCount)` (`PublicUserProfileResponse.cs:3-10`), mapped in `MapsterConfiguration.cs:35-45`. Has no `Id`; `address.entityId` supplies it | No email or phone |
| Visibility | `GetPublicUserProfileHandler.cs:16-25` only null-checks. **Soft-deleted and blocked users are returned** | **Leak** (Open Question 3) |
| `GET api/Users/me` | `[Authorize]`, adds `Email` (`UserProfileResponse.cs:3-12`) | Private |
| Private fields | `Credentials.Email`, `PhoneNumber`, tokens, `Roles`, `RefreshTokens` (`User.cs:11-27`) | Never exposed publicly |
| Privacy flag | None exists (`User.cs:9-27`; grep for `Visibility/IsPublic/IsPrivate` returns nothing) | — |
| Status flags | `IsSoftDelete` (`User.cs:20`, set by `SetSoftDelete` `:163-166`), `IsBlocked` (`:21`, reversible `SetBlocked` `:168-173`, revokes sessions) | — |
| User reviews in Shops | `GET api/users/{userId:guid}/reviews` is `[Authorize]` (`UserReviewsController.cs:11-12`); gateway `shops-users-reviews-route` (`appsettings.json:248-251`) | Authenticated only |
| Review author in shop reviews | `ReviewDto { UserId, Username }` (`ReviewDto.cs:5-8`); Shops keeps `Review.UserName` in sync via `UserNameChangedEvent` (`Shops.Infrastructure/Consumers/UserNameChangedEventHandler.cs:12-14`) | The username and GUID are already public |
| Response caching | No `[ResponseCache]` in AccountService. `CachedUserRepository` exists but is **never registered** (`Account.Persistence/DependencyInjection.cs:44` registers plain `UserRepository`; grep finds no other reference) | No profile cache to invalidate |

## Evidence: username rules and slug policy (Q2)

| Fact | Evidence |
|---|---|
| Charset `^\p{L}[\p{L}\p{Nd}._]*$`: any Unicode letter, digits, dot, underscore; must start with a letter | `Username.cs:41` |
| Length 3–30 after `Trim()`; case preserved | `Username.cs:8-9,25-28`, `BusinessConstants.cs:11` |
| Unique index is case-sensitive (plain `varchar(30)`, default collation), so `Ivan` and `ivan` can coexist | `UserConfiguration.cs:16-21`, snapshot `:249-259`, `QueryUserRepository.cs:43-46` |
| Mutable at any time: `PATCH api/Users/me/username`, no cooldown, no uniqueness pre-check (the DB index → `ConflictException` 409); publishes `UserNameChangedEvent` | `UsersController.cs:163-179`, `UpdateProfileUsernameHandler.cs:17-36`, `GlobalExceptionHandler.cs:64` |
| Google users get a username **derived from the email local part** (`ivan.petrov1985@gmail.com` → `ivan.petrov1985`) or that value plus a 4-hex suffix | `User.cs:68-98`, `ExternalAuthService.cs:34-36,46-64` |
| Unconfirmed email users cannot log in | `AuthService.cs:26-29` |

**Spike results** (`PublicSlug.Generate` on EF-free Shared.Domain, run this session, deleted):

| Usernames | Slug |
|---|---|
| `ivan`, `Ivan`, `Иван` | `ivan` |
| `ivan.petrov`, `Ivan_Petrov`, `ivan__petrov` | `ivan-petrov` |
| `Алёна_Ш`, `Мікалай` | `alyona-sh`, `mikalay` |
| `Zoë`, `Łukasz`, `Ğökhan`, `Straße` | `zoe`, `lukasz`, `gokhan`, `strasse` |
| `İlker` | `lker` (the Turkish dotted İ is dropped; a pre-existing shared-rule quirk) |
| `admin`, `settings`, `search`, `null`, `by_slug`, `public_addresses` | `place` (reserved) |
| `日本語`, `Ελένη`, `ნინო`, `محمد` | `place` (unsupported script) |
| 30× `Щ` | 100-char `shch…` (truncated, valid) |
| `reviews`, `users`, `user123` | unchanged, valid |

GUID-shaped slugs cannot survive: a 32-hex output (`æ` → `ae` makes it reachable) fails `IsValid` and falls back to `place` (`PublicSlug.cs:19-20,54`). Candidate suffixes are `-<8hex>`, `-<12hex>`, `-<16hex>` of the owner GUID (`PublicSlug.cs:58-73`).

**Conclusion:** The username cannot be used 1:1. Only all-lowercase ASCII-alphanumeric usernames would be identical. A registry-backed derived slug is required.

**Policy options.** User decision, Open Question 1.

| Option | Behavior | Pros | Cons |
|---|---|---|---|
| **A (recommended)**: follow username, keep aliases | Allocate from the username at creation. A username change is an owner-authorized rename: the new canonical comes from the new username, the old slug becomes an alias owned by the user, A→B→A restores | URL matches the displayed username. A Google user who later picks a real username stops having the email-derived handle as the canonical/share URL. Old links keep working. Matches the spec rename semantics ("retains previous addresses", PUBLIC-SLUGS.md:11) | Every change permanently reserves one more slug, a squatting vector with no cooldown today (Open Question 1b). Anyone with an old link learns the new username. A different user who later takes the freed username gets `ivan-<8hex>`, because `ivan` stays reserved |
| B: stable, never follows | Allocate once at creation; username changes never rename | Smallest diff (no rename branch); no reservation growth | The old or email-derived name stays the canonical URL forever. That is a privacy regression and violates user expectation |
| C: follow username, retire old slug | Like A, but the old canonical becomes a tombstone | No old→new linkage | Old links 404; A→B→A yields `ivan-<hex>`; contradicts spec alias retention |

## Evidence: reuse of the Shops coordinator (Q3)

| Piece | Location | Reusable from Account? |
|---|---|---|
| `PublicSlug`, `PublicAddress` base, `PublicAddressState` | `CoffeePeek.Shared.Domain/PublicAddresses/*` | **Yes, unchanged.** `Account.Domain` already references Shared.Domain |
| `PublicAddressMetadata/Envelope/BackfillItem/BackfillResult` | `CoffeePeek.Contract/Dtos/PublicAddresses/PublicAddressDtos.cs:3-8` | **Yes, unchanged.** Transitively referenced via Shared.Web → Contract |
| `PublicAddressKind`, `PublicAddressOptions`, `PublicAddressException`, `IPublicAddressService` | `CoffeePeek.Shops.Application/PublicAddresses/IPublicAddressService.cs:7-25` | No (Shops bounded context) |
| `PublicAddressConfiguration.Configure<T>` | `Shops.Persistance/Configuration/PublicAddressConfiguration.cs:47-68`, `internal static` | No. Copy it (about 20 lines) |
| `PublicAddressTransaction.RunAsync` | `Shops.Persistance/PublicAddresses/PublicAddressTransaction.cs:8-41`, typed to `ShopsDbContext` | No. Copy it (about 35 lines) |
| `PublicAddressWriter` (Initialize/FindCandidate/Retire/Audit), `PublicAddressService` | `PublicAddressWriter.cs:12-113`, `PublicAddressService.cs:13-245` | No. Four-kind switches and audit are Shops-specific |
| `PublicAddressAudit` | `Shops.Persistance/PublicAddresses/PublicAddressAudit.cs:6-18` | Not wanted: it retains `OwnerId`, slugs and `ActorId` after retirement (see Pitfall 5) |

**Recommendation (Open Question 4): an Account-local single-kind copy.** It needs about 5 small files. Nothing in Shops changes, so phases 1–3 (verified, frozen) carry no regression risk. The Account version is also *simpler*: one kind, no `PublicAddressKind` switch, no audit table, no admin rename/history.

The alternative is to move the two DbContext-agnostic helpers (`RunAsync` and `Configure<T>`) into `CoffeePeek.Shared.Persistence`. That gives one source of truth for the schema invariants, but it edits four verified Shops files. If the user prefers it, it is still small: change `ShopsDbContext db` to `DbContext db` and add a lock-key parameter.

To stop the copy drifting, **pin the schema with a model test** that mirrors `PublicAddressPersistenceTests.cs:17-43`: collation `C`, max length 100, concurrency token, unfiltered unique slug index, canonical filtered index `"State" = 1`, the four check constraints and the RESTRICT FK.

## Evidence: lifecycle hooks in Account (Q4)

| Path | Evidence | Address action (recommended) |
|---|---|---|
| Email registration | `RegisterUserHandler.cs:36-47`: `User.Register` → `QueryUserRepository.Add` → `UnitOfWork.SaveChangesAsync` | Hook: `Added` User → initialize from `Username` (when enabled) |
| Google OAuth creation | `ExternalAuthService.cs:34-43` adds the user; `OAuthLoginHandler.cs:46-60` saves (with a conflict-retry path) | Same `Added` branch |
| Username change | `UpdateProfileUsernameHandler.cs:17-24`: tracked `GetById`, `UpdateUsername`, `UserRepository.Update` (= `DbSet.Update`, `UserRepository.cs:25-29`), save | Hook: `Modified` with **original ≠ current** `Username` → follow rename (Policy A) |
| Account deletion confirmation | `ConfirmAccountDeletionHandler.cs:62-73`: `SetSoftDelete` + revoke + save | Hook: `IsSoftDelete` false→true → retire all (**always**, even when disabled) |
| Admin delete | `AdminDeleteUserCommand.cs:30-38`: `SetSoftDelete` + save | Same retire branch |
| Admin block/unblock | `BlockUserCommand.cs:30-39` (deleted users cannot be blocked, `:34-35`) | **None.** The visibility filter hides blocked users; blocking is reversible, so never retire |
| Physical delete | None exists (grep: no `Users.Remove`/`ExecuteDelete`); `AccountDeletionRequestedV1` is published (`ConfirmAccountDeletionHandler.cs:81-86`) but has no consumer | Hook also retires on `EntityState.Deleted`, so a future hard delete passes the RESTRICT FK |
| Future anonymization (rewrites `Username` of a deleted user) | Deferred (STATE.md:85) | The follow branch must skip `IsSoftDelete` users |
| Stats consumers (`ReviewAddedHandler`, `CheckinCreatedHandler`) | `Account.Infrastructure/Consumers/ReviewAddedHandler.cs:11-15`: tracked `Modified` user | None. They must not trigger the hook (no username or deletion transition) |
| SaveChanges hook point | `AccountDbContext.cs:10-35` has **no** override today. `UnitOfWork<T>.SaveChangesAsync` wraps it and maps `DbUpdateException` → `ConflictException` (`Shared.Persistence/Data/UnitOfWork.cs:10-20`). The Wolverine EF middleware calls `SaveChangesAsync` again after the handler, inside its own transaction (`RegisterUserCommandHandler748332044.cs`, "Added by EF Core Transaction Middleware") | Override `SaveChangesAsync(bool, ct)` as `ShopsDbContext.cs:29-49` does |
| Codegen coupling | Generated handlers construct `new UserRepository(accountDbContext)` and `new UnitOfWork<AccountDbContext>(...)` and resolve `AccountDbContext` via service location (`AlwaysUseServiceLocationFor`, `WolverineModule.cs:48-51`; `InfrastructureExtensions.cs:27-30`) | Adding an optional `IOptions<PublicAddressOptions>?` ctor parameter to the DbContext needs **no codegen**. Changing repository or handler signatures would |

**Verified by spike** (InMemory, real `AccountDbContext` + `UserRepository`, deleted): after `GetById` (tracked), `UpdateUsername`, `SetSoftDelete` and `UserRepository.Update`:
- `State = Modified`.
- `Username` original is `Ivan.Petrov` and current is `petr`.
- `IsSoftDelete` original is `False` and current is `True`.
- **The untouched `About.IsModified` is `True`.**

So the hook must compare **original vs current values, never `IsModified`**.

## Evidence: privacy, enumeration, caching (Q5)

- **Payload:** reuse `PublicUserProfileResponse` as `data` in `PublicAddressEnvelope<PublicUserProfileResponse>`. It carries no email, phone, roles or sessions. Do not create a new DTO.
- **Enumeration:** by-slug returns 200 or 404, which reveals whether a username-like handle exists. This is not a new oracle:
  - Registration already reports "Username already exists" (`RegisterUserHandler.cs:26-27`).
  - Anonymous `GET api/Users/exists?email=` already reveals email existence (`UsersController.cs:50-60`). This is pre-existing and out of scope; do not fix it here.

  Keep by-slug on the same `auth-endpoints` 20/min per-IP limiter as the rest of `/api/Users`. The batch endpoint needs GUIDs, which are only known from public review data.
- **Identical 404s:** unknown, retired, deleted and blocked must be indistinguishable: same status, empty body. If the data read throws `NotFoundException`, the controller must return `NotFound()` rather than a descriptive `ErrorResponse`.
- **Caching:** there is no Redis profile cache (`CachedUserRepository` is unregistered) and no response caching attribute. The new endpoints use `[ResponseCache(NoStore = true, Location = None)]` like phase 2/3. **No cache invalidation is needed.**
- **Indexing risk:** Google users' email-derived usernames become crawlable canonical URLs (`/users/ivan-petrov1985`). The username is already public in reviews, but a crawlable URL increases exposure (Open Question 8).
- **Alias linkage:** under Policy A, an old slug resolves with `isAlias: true` and the new `canonicalPath`, which reveals the new username to anyone who has the old link. This is inherent to alias retention.

## Evidence: cross-service (Q6)

- Shops shows authors as `ReviewDto.UserId` + `Username` (`ReviewDto.cs:5-8`). The frontend can link authors with **one** call to `GET api/Users/public-addresses?ids=` (≤100) per page. **No Shops change, no new event, no denormalized slug.**
- A slug copy in Shops would need a new Contract event, a consumer, alias and visibility sync, and deletion handling. That is speculative; defer it.
- Moderation has only admin-facing `ModerationReviewDto.UserName`. No change.

## Architecture Patterns

### System Architecture Diagram

```
                ┌────────────── writes (existing handlers, unchanged) ──────────────┐
Register / Google OAuth / PATCH me/username / confirm deletion / admin delete
        │ Wolverine handler (generated; new UserRepository(ctx); UnitOfWork)
        ▼
UnitOfWork.SaveChangesAsync ──► AccountDbContext.SaveChangesAsync (NEW override)
        │  DetectChanges; inspect Entries<User>()
        ├─ nothing relevant ─────────────────────────────► base.SaveChangesAsync
        ▼ retiring (IsSoftDelete false→true | Deleted)  or  enabled && (Added | Username orig≠cur)
ProfileAddressTransaction.RunAsync (ambient Wolverine tx → savepoint | own tx; pg_advisory_xact_lock)
        ▼
ProfileAddressWriter
  ├─ retiring → retire ALL rows (always, even when disabled) → tombstones (OwnerId NULL, slug only)
  ├─ enabled && Added → initialize: FindCandidate(stem(username)) → canonical rev 1
  └─ enabled && username changed && !deleted → owner-aware candidate:
         own canonical → no-op | own alias → restore | free → new canonical
         demote current (MakeAlias) → flush via base save → promote → base save
        ▼
PostgreSQL cpaccountdb: UserPublicAddresses (UNIQUE Slug, UNIQUE OwnerId WHERE State=1, FK→Users RESTRICT, 4 CHECKs)

                ┌────────────── reads (public, anonymous) ──────────────┐
Frontend → Gateway /api/Users/by-slug/{slug} (NEW dedicated route, auth-endpoints 20/min)
         → Gateway /api/Users/{**remainder} (existing) for public-addresses?ids= and {id}/public-address
        ▼ (503 when disabled; 400 invalid slug)
ProfileAddressService.ResolveAsync(slug)
   slug row → OwnerId → visible? (Users: Id && !IsSoftDelete && !IsBlocked) → current canonical
        ▼ null → 404 (identical for unknown/retired/deleted/blocked)
IMessageBus.InvokeAsync(GetPublicUserProfileCommand(entityId))  [existing handler; re-checks visibility]
        ▼ NotFound → 404
PublicAddressEnvelope<PublicUserProfileResponse>(data, {entityId, slug, canonicalPath:"/users/{slug}", revision, isAlias})

Admin → Gateway /api/admin/{**} (Admin) → POST api/admin/users/public-addresses/backfill?afterId&limit&dryRun (Admin)
```

### Recommended Project Structure (new or changed files only)

```
CoffeePeek.Account.Domain/Entities/PublicAddressAggregate/UserPublicAddress.cs   # new; clone of CoffeeShopPublicAddress
CoffeePeek.Account.Application/
├── Common/Options/PublicAddressOptions.cs            # new; Enabled (section "PublicAddresses")
├── Common/Interfaces/IProfileAddressService.cs       # new; Resolve / GetByIds / Backfill
└── Features/User/GetProfile/GetPublicUserProfileHandler.cs  # body only (Open Question 3): 404 for deleted/blocked
CoffeePeek.Account.Persistence/
├── Configuration/AccountDbContext.cs                 # DbSet, optional IOptions ctor param, SaveChanges override
├── Configuration/UserPublicAddressConfiguration.cs   # new; registry schema + FK Restrict
├── PublicAddresses/ProfileAddressTransaction.cs      # new; copy of RunAsync for AccountDbContext
├── PublicAddresses/ProfileAddressWriter.cs           # new; hook branches, candidate search, retire
├── PublicAddresses/ProfileAddressService.cs          # new; lookup, visibility, backfill
├── DependencyInjection.cs                            # Configure<PublicAddressOptions>, AddScoped service
└── Migrations/<ts>_AddUserPublicAddresses(.Designer).cs + snapshot   # via make mig-acc
CoffeePeek.AccountService/Controllers/
├── PublicUserAddressesController.cs                  # new; [Route("api/Users")] by-slug, public-addresses, {id:guid}/public-address
└── AdminUserPublicAddressesController.cs             # new; [Route("api/admin/users/public-addresses")] POST backfill, Admin
CoffeePeek.Gateway/appsettings.json                   # + "account-Users-by-slug-route" /api/Users/by-slug/{slug}
docs/api/public-addresses.md                          # Users section, policy, deletion, prerequisite, live gate
Tests:
  CoffeePeek.Account.Domain.Tests/UserPublicAddressTests.cs
  CoffeePeek.Account.Infrastructure.Tests/Persistence/UserPublicAddressPersistenceTests.cs   (Npgsql model)
  CoffeePeek.Account.Infrastructure.Tests/Persistence/UserPublicAddressServiceTests.cs       (InMemory lifecycle)
  CoffeePeek.Account.Infrastructure.Tests/PublicUserAddressControllerTests.cs                (reflection + fakes)
  CoffeePeek.Account.Application.Tests/Features/User/GetProfile/GetProfileHandlerTests.cs    (extend)
  CoffeePeek.Gateway.Tests/ReverseProxyRouteConfigurationTests.cs                            (extend)
```

### Pattern 1: Registry entity and schema (clone of phase 1)
```csharp
// Source: CoffeePeek.Shops.Domain/.../CoffeeShopPublicAddress.cs and PublicAddressConfiguration.cs:47-68
public sealed class UserPublicAddress : PublicAddress
{
    private UserPublicAddress() { }
    private UserPublicAddress(Guid ownerId, string slug, long revision, DateTime now) : base(ownerId, slug, revision, now) { }
    public static UserPublicAddress Create(Guid ownerId, string slug, long revision, DateTime now) => new(ownerId, slug, revision, now);
}

public sealed class UserPublicAddressConfiguration : IEntityTypeConfiguration<UserPublicAddress>
{
    private const string Table = "UserPublicAddresses";
    public void Configure(EntityTypeBuilder<UserPublicAddress> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Slug).IsRequired().HasMaxLength(PublicSlug.MaxLength).UseCollation("C");
        builder.Property(a => a.Revision).IsConcurrencyToken();
        builder.HasIndex(a => a.Slug).IsUnique();                                  // reserves every state
        builder.HasIndex(a => a.OwnerId).IsUnique().HasFilter("\"State\" = 1");    // at most one canonical
        builder.HasIndex(a => new { a.OwnerId, a.State });
        builder.HasOne<User>().WithMany().HasForeignKey(a => a.OwnerId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(Table, t =>
        {
            t.HasCheckConstraint($"CK_{Table}_Slug", "\"Slug\" ~ '^[a-z0-9]+(-[a-z0-9]+)*$' AND length(\"Slug\") BETWEEN 1 AND 100");
            t.HasCheckConstraint($"CK_{Table}_Lifecycle", "(\"State\" IN (1, 2) AND \"OwnerId\" IS NOT NULL) OR (\"State\" = 3 AND \"OwnerId\" IS NULL)");
            t.HasCheckConstraint($"CK_{Table}_Revision", "\"Revision\" > 0");
            t.HasCheckConstraint($"CK_{Table}_Timestamps", "\"UpdatedAtUtc\" >= \"CreatedAtUtc\"");
        });
    }
}
```
`AccountDbContext.OnModelCreating` uses `ApplyConfigurationsFromAssembly` (`AccountDbContext.cs:20`), so the new configuration is picked up automatically. `HasBaseType(null)` is not needed, because Account has only one `PublicAddress` subtype. **Do not add a navigation** from the registry row to `User`: the tombstone must stay structurally unlinkable. Generated names (`FK_UserPublicAddresses_Users_OwnerId`, 36 chars) stay under PostgreSQL's 63-char limit.

### Pattern 2: SaveChanges hook (no handler changes)
```csharp
// Source: pattern from ShopsDbContext.cs:29-49; detection verified by the DbSet.Update spike
public class AccountDbContext(DbContextOptions<AccountDbContext> options, IOptions<PublicAddressOptions>? addressOptions = null)
    : DbContext(options)
{
    public bool PublicAddressesEnabled => addressOptions?.Value.Enabled == true;
    public DbSet<UserPublicAddress> UserPublicAddresses { get; set; }

    public override int SaveChanges(bool acceptAllChangesOnSuccess) =>
        SaveChangesAsync(acceptAllChangesOnSuccess).GetAwaiter().GetResult();

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default)
    {
        ChangeTracker.DetectChanges();
        var users = ChangeTracker.Entries<User>().ToArray();
        // Retirement always runs (privacy + RESTRICT FK); allocation/rename only when enabled.
        if (!users.Any(e => ProfileAddressWriter.IsRetiring(e) ||
                            (PublicAddressesEnabled && ProfileAddressWriter.NeedsAddress(e))))
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);
        return ProfileAddressTransaction.RunAsync(this,
            () => ProfileAddressWriter.SaveAsync(this, users, acceptAllChangesOnSuccess, ct), ct);
    }

    // Used by the writer for the demotion flush and the final save; never re-enters the hook.
    internal Task<int> SaveCoreAsync(bool accept, CancellationToken ct) => base.SaveChangesAsync(accept, ct);
}

internal static class ProfileAddressWriter
{
    // Compare original vs current: UserRepository.Update marks EVERY property IsModified.
    public static bool IsRetiring(EntityEntry<User> e) =>
        e.State == EntityState.Deleted ||
        (e.State == EntityState.Modified && e.Entity.IsSoftDelete &&
         e.Property(nameof(User.IsSoftDelete)).OriginalValue is false);

    public static bool NeedsAddress(EntityEntry<User> e) => !e.Entity.IsSoftDelete &&
        (e.State == EntityState.Added ||
         (e.State == EntityState.Modified && !Equals(e.Property(nameof(User.Username)).OriginalValue, e.Entity.Username)));
}
```
The writer's `SaveAsync` works in this order:
1. Snapshot the entries, then retire every retiring owner. This always runs.
2. `if (!db.PublicAddressesEnabled) return db.SaveCoreAsync(accept, ct)`.
3. Initialize the `Added` users.
4. For username changes (Policy A), run the owner-aware candidate search (Pattern 3).
5. If any canonical was demoted, flush once with `db.SaveCoreAsync(true, ct)`, then promote or restore.
6. `return affected + await db.SaveCoreAsync(accept, ct)`.

`ProfileAddressTransaction.RunAsync` is a copy of `PublicAddressTransaction.cs:8-41` with `AccountDbContext` and its own lock key, for example `7402100401`. Advisory locks are keyed per database and Account uses `cpaccountdb` (`deploy/docker-compose.yml:201`), so reusing the Shops key would also work; a distinct key is clearer [ASSUMED: PG advisory-lock scoping].

### Pattern 3: Owner-aware candidate (Policy A rename)
```csharp
// A candidate owned by the same user is not a conflict: own canonical → no-op, own alias → restore (A→B→A).
// Without this, a suffixed user ("ivan-petrov-3fa2b1c4") changing "ivan.petrov" → "Ivan.Petrov" would skip
// their OWN canonical as "taken" and mint "ivan-petrov-3fa2b1c45d6e".
for (var attempt = FirstAttempt(stem); attempt < 6; attempt++)
{
    var slug = PublicSlug.Candidate(stem, ownerId, attempt);
    var row = db.UserPublicAddresses.Local.FirstOrDefault(a => a.Slug == slug)
              ?? await db.UserPublicAddresses.SingleOrDefaultAsync(a => a.Slug == slug, ct);
    if (row is null || row.OwnerId == ownerId) return (slug, row);   // free, or already ours
}
throw new ConflictException("Could not allocate a unique profile address.");
```
Stem: `PublicSlug.Generate(username)`. When it returns the `place` fallback, use the stem `user` and start at attempt 1 (always suffixed, `user-3fa2b1c4`), so that no single person owns a bare `place` or `user`. This is Claude's discretion; a profile called `place` is semantically wrong.

### Pattern 4: Public endpoints (mirrors `PublicRoasterAddressesController.cs:11-36`)
```csharp
[ApiController]
[Route("api/Users")]
[Tags("User")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PublicUserAddressesController(IProfileAddressService addresses, IMessageBus bus) : ControllerBase
{
    /// <summary>Public profile by readable address (canonical or alias).</summary>
    [HttpGet("by-slug/{slug}")]
    public async Task<IActionResult> GetBySlug(string slug, CancellationToken ct)
    {
        var address = await addresses.ResolveAsync(slug, ct);          // visibility-filtered; null → 404
        if (address is null) return NotFound();
        try
        {
            var response = await bus.InvokeAsync<Response<PublicUserProfileResponse>>(
                new GetPublicUserProfileCommand(address.EntityId), ct);  // existing handler; re-checks deleted/blocked (Open Question 3)
            return response is { IsSuccess: true, Data: not null }
                ? Ok(new PublicAddressEnvelope<PublicUserProfileResponse>(response.Data, address)) : NotFound();
        }
        catch (NotFoundException) { return NotFound(); }                // identical 404, no descriptive body
    }
    [HttpGet("public-addresses")]           /* GetByIdsAsync(ids): 1–100 non-empty, visible canonical only */
    [HttpGet("{id:guid}/public-address")]   /* single legacy GUID → 404 when not visible */
}
```
Route safety inside AccountService:
- `UsersController` owns `api/Users/{id:guid}`, `me`, `exists` and `password/*` (`UsersController.cs:36-348`).
- `by-slug/{slug}` has two segments and a literal first segment; `public-addresses` fails the `:guid` constraint; `{id:guid}/public-address` has a distinct literal.
- No collisions. Prove this with a reflection test, as `PublicAddressApiContractTests.cs` does.

### Pattern 5: Admin backfill only
`POST api/admin/users/public-addresses/backfill?afterId=&limit=50&dryRun=true` with `[Authorize(Policy = RoleConsts.Admin)]`. The gateway `account-admin-route` already requires Admin (`appsettings.json:170-174`). There is no collision with `AdminUsersController` (`api/admin/users`, `{id:guid}` templates).

The owner listing:
```csharp
db.Users.AsNoTracking()
    .Where(u => !u.IsSoftDelete && (afterId == null || u.Id > afterId))
    .OrderBy(u => u.Id)
    .Take(limit + 1)
    .Select(u => new { u.Id, u.Username })
```
It includes blocked users: they are hidden by visibility, and unblocking must not require a new allocation. Semantics are the same as `PublicAddressService.cs:126-162` (dry-run writes nothing, replay never renames, `nextCursor`).

Do **not** add admin initialize, rename or history. Under Policy A, the next username change would override an admin rename, and history needs an audit table (see Open Question 5).

### Anti-Patterns to Avoid
- **A `Slug` column on `User`, or putting the slug in `PublicUserProfileResponse`/`GetProfile`.** This bypasses alias and tombstone reservation, and changes existing DTOs.
- **Detecting changes with `Property(...).IsModified`.** `DbSet.Update` sets every property to modified (spike). Compare original vs current values.
- **Calling `db.SaveChangesAsync` from inside the writer.** It re-enters the hook and re-processes the still-Modified user. Use `base.SaveChangesAsync` via `SaveCoreAsync`.
- **Allocating or renaming in Wolverine handlers.** That changes handler signatures (codegen) and misses Google or other write paths.
- **Retiring on block.** Blocking is reversible and retirement is not. Hide blocked users through visibility instead.
- **Copying the Shops audit table.** It keeps `OwnerId`, `ActorId` and the slug history after retirement, which breaks the tombstone privacy rule.
- **Injecting scoped services (for example `IUserContext`) into `AccountDbContext`.** Aspire's `AddNpgsqlDbContext` pools the context in Development (Pitfall 7).
- **Denormalizing slugs into Shops reviews.** The batch endpoint is enough.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Slugs/transliteration/reserved/GUID rejection | Username-specific slug rules | `PublicSlug.Generate/Candidate/IsValid` (`PublicSlug.cs`) | Versioned shared rules; RU/BY transliteration already tested |
| Row lifecycle | Custom state flags | `PublicAddress.MakeAlias/Restore/Retire` (`PublicAddress.cs:29-49`) | Monotonic revision, UTC, and `OwnerId = null` on retire are already enforced |
| Transaction + serialization | New locking | Copy of `PublicAddressTransaction.RunAsync` | Handles savepoints inside Wolverine's ambient tx, the advisory lock, and tracker clear on rollback |
| Response envelope/metadata | New profile DTOs | `PublicAddressEnvelope<PublicUserProfileResponse>`, `PublicAddressMetadata` | Same frontend contract as shops/cities |
| Profile data read | New query/handler | Existing `GetPublicUserProfileCommand` via `IMessageBus` | No codegen; one mapping path (Mapster `MapsterConfiguration.cs:35-45`) |
| 400/409/503 | New exception type | `ValidationException` (400), `ConflictException` (409, `GlobalExceptionHandler.cs:64`), `ServiceUnavailableException` (503) from `CoffeePeek.Shared.Kernel/Exceptions` | Already mapped by `GlobalExceptionHandler` |

**Key insight:** the invariants (reservation, at most one canonical, retirement) already live in shared code and the database. The Account-specific risks are **change detection** (spike-verified pitfalls), **privacy of deleted and blocked users**, and **username-policy semantics**, not missing slug logic.

## Runtime State Inventory

(This phase is not a rename, but it adds a registry that existing production users must be backfilled into.)

| Category | Items Found | Action Required |
|----------|-------------|------------------|
| Stored data | Existing `cpaccountdb."Users"` rows get no address when the migration runs. Soft-deleted users must never be backfilled | Operational backfill via `POST api/admin/users/public-addresses/backfill?dryRun=true` → review → write (phase 5). Nothing runs in this phase |
| Live service config | `PublicAddresses` is set in no appsettings, compose or AppHost file for Account (grep of `CoffeePeek.AccountService/appsettings.json`, `deploy/docker-compose.yml`). It defaults to false | None; stays default-off. The flag is **per service** (`PublicAddresses__Enabled` on the `account` container, independent of Shops) |
| OS-registered state | None: `deploy/scripts/*` has no scheduled jobs touching users | None |
| Secrets/env vars | The design-time factory requires `PostgresCpOptions:ConnectionString` (`AccountDbContextFactory.cs:20-25`) even for `migrations add` | Set a dummy value for generation (verified: `has-pending-model-changes` with a dummy value → "No changes have been made to the model since the last migration.") |
| Build artifacts | Wolverine `CoffeePeek.AccountService/Internal/Generated/WolverineHandlers/` (45 files) is unaffected by the recommended design (body-only handler change, new controllers, DbContext ctor via service location) | None. If any handler signature changes, run `dotnet run -- codegen write` |

## Common Pitfalls

### Pitfall 1: `IsModified` is always true after `UserRepository.Update`
**What goes wrong:** every About, phone, avatar or stats save looks like a username change or deletion. That causes needless locks, spurious renames, or retiring an already-deleted user on each save.
**Why:** `UserRepository.Update` calls `DbSet.Update` (`UserRepository.cs:25-29`), which marks all properties modified [VERIFIED: spike, `About.IsModified=True` although unchanged].
**How to avoid:** compare `OriginalValue` with the current value (Pattern 2). Add regression tests: an About update and a stats increment create no address rows and no revision bump.

### Pitfall 2: Re-entering the hook from inside the writer
**What goes wrong:** infinite recursion or a double rename, when the demotion flush calls the overridden `SaveChangesAsync` while the user entry is still Modified.
**How to avoid:** the writer only calls `base.SaveChangesAsync` (`SaveCoreAsync`). The demotion flush is still required, because EF cannot order an UPDATE `State 1→2` before an INSERT `State=1` for the *filtered* owner index. Phase 2 flushes for the same reason (`PublicAddressService.cs:98-103`). Proof requires live PG.

### Pitfall 3: Case- and punctuation-only username changes and own-suffix collisions
**What goes wrong:** `Ivan` → `ivan` or `ivan.petrov` → `ivan_petrov` mints a new revision or alias; a suffixed owner renames to a longer suffix.
**How to avoid:** use the owner-aware candidate (Pattern 3) and treat "same slug as current canonical" as a no-op. Tests: no revision bump; own alias restored (A→B→A); another owner's alias or tombstone forces a suffix.

### Pitfall 4: Gateway captures `/api/Users/by-slug/reviews`
**What goes wrong:** a user whose slug is `reviews` (a valid slug and a valid username) can never be resolved. The request goes to Shops and returns 404.
**Why:** `shops-users-reviews-route` `/api/users/{userId}/reviews` (`appsettings.json:248-251`) outranks `account-Users-route` `/api/Users/{**remainder}` (`:77-81`). A parameter segment beats a catch-all, and YARP matching is case-insensitive [CITED: learn.microsoft.com/aspnet/core/fundamentals/routing, "Route template precedence"; YARP routes are ASP.NET Core endpoints, github.com/dotnet/yarp `ProxyEndpointFactory.cs`].
**How to avoid:** add the gateway route `account-Users-by-slug-route` → `account-cluster`, `Match.Path = "/api/Users/by-slug/{slug}"`, `RateLimiterPolicy = "auth-endpoints"`. The literal `by-slug` beats `{userId}`. Add a JSON config test in `ReverseProxyRouteConfigurationTests.cs`. The alternative is adding `reviews` to `PublicSlug.Reserved`, which changes a shared rule; see Open Question 6.

### Pitfall 5: Private data surviving retirement
**What goes wrong:** the "no private data in tombstones" rule is broken indirectly, by an audit row that keeps `OwnerId` plus the full slug history, or by a new column (display name, email) added to the registry.
**How to avoid:** no audit table (Open Question 5). Retirement nulls `OwnerId` on **every** row (canonical and aliases, `PublicAddress.cs:33-38`). Add these tests:
- After deletion, `UserPublicAddresses.Count(a => a.OwnerId == userId) == 0`, and every former slug is a tombstone.
- A model test that the entity has exactly the 7 base properties (`Id, OwnerId, Slug, State, Revision, CreatedAtUtc, UpdatedAtUtc`) and no navigation.

**Residual risk** (document it; spec-accepted): the slug itself is derived from the username, and for Google users from the email local part. It stays reserved forever.

### Pitfall 6: Retirement runs even when disabled, so the migration is a deployment prerequisite
**What goes wrong:** with `PublicAddresses__Enabled=false` but the migration not applied, account deletion (confirm or admin) fails with `42P01`, which surfaces as 500.
**Why:** this is the same design and the same lesson as phase 3 WR-02 (`03-REVIEW.md:93-111`). Retirement must not depend on the flag.
**How to avoid:** document `<ts>_AddUserPublicAddresses` under the doc's "Deployment prerequisite (independent of the flag)". `deploy/scripts/apply-migrations.sh:57-60` already migrates Account before restart.

### Pitfall 7: DbContext pooling in Development
**What goes wrong:** you assume an extra DbContext ctor parameter breaks Aspire, or you inject a scoped service into it.
**Facts:** Aspire `AddNpgsqlDbContext` "Enables db context pooling" [CITED: aspire.dev package docs, Aspire.Npgsql.EntityFrameworkCore.PostgreSQL]. A spike on EF Core 10.0.5, `AddDbContextPool` with `Ctx(DbContextOptions<Ctx>, IOptions<Opt>? = null)`, **resolved and injected the options** (`Enabled=True`) [VERIFIED: spike]. This matches the working Shops pattern (`ShopsDbContext.cs:20`, `Shops.Persistance/DependencyInjection.cs:32-40`).
**How to avoid:** inject only singletons (`IOptions<T>`). Register `services.Configure<PublicAddressOptions>(builder.Configuration.GetSection("PublicAddresses"))` in `Account.Persistence/DependencyInjection.cs`. Keep the parameter optional so `AccountDbContextFactory.cs:29` and the tests (`new AccountDbContext(options)`) still compile.

### Pitfall 8: InMemory cannot prove constraints, rollback or locking
**What goes wrong:** you conclude that concurrent `Ivan`/`ivan` registrations get distinct slugs, or that a username unique violation rolls back the demotion.
**Why:** the transaction helper skips non-relational providers (`PublicAddressTransaction.cs:11`). InMemory enforces no unique, filtered, FK or check constraints, has no collation and no advisory lock.
**How to avoid:** list these as live-gate items (Validation Architecture). Keep `Enabled=false`. Avoid `ExecuteUpdate`/`ExecuteDelete` in the address code: InMemory does not support them.

### Pitfall 9: ChangeTracker.Clear() on rollback affects callers
**What goes wrong:** after a failed save inside `RunAsync`, every tracked entity is detached (`PublicAddressTransaction.cs:34-38`).
**Callers affected:**
- `GoogleLoginHandler` re-fetches the user after a `ConflictException` (`OAuthLoginHandler.cs:50-59`), so the retry still works. It is arguably better than today, because the failed duplicate user is no longer tracked and the ambient transaction is restored to the savepoint.
- `RegisterUserHandler` maps the conflict to "Email already exists" (`:49-53`).
- `UpdateUsernameHandler` gets `ConflictException` → 409. The Wolverine `TransientDbConflict` retry (`WolverineModule.cs:99-101`) re-runs the handler, which fails the same way.

**How to avoid:** add a test for "username taken by another user → 409, address unchanged" (a live-gate item for the rollback part).

### Pitfall 10: Migration generation environment
`make mig-acc n=AddUserPublicAddresses` needs `PostgresCpOptions__ConnectionString` (dummy is fine; no connection is made). The baseline is clean [VERIFIED: `has-pending-model-changes` this session]. The new migration must contain only `CreateTable UserPublicAddresses` + 3 indexes + FK + 4 checks. Anything else is drift.

### Pitfall 11: Shared-rule quirks are not to be fixed here
`İ` is dropped (`İlker` → `lker`), and BY `г` → `g` (phase 3). Changing `PublicSlug` affects every kind, and the spec says rules are versioned. Add username regression rows to a test so generation stays stable, and leave the rules unchanged.

## Code Examples

### Visibility and resolve (single kind)
```csharp
// Source: PublicAddressService.cs:30-38 reduced to one kind
public async Task<PublicAddressMetadata?> ResolveAsync(string slug, CancellationToken ct)
{
    RequireEnabled();                                             // 503 ServiceUnavailableException
    var normalized = slug.ToLowerInvariant();
    if (!PublicSlug.IsValid(normalized)) throw new ValidationException("Invalid public address.");
    var row = await db.UserPublicAddresses.AsNoTracking().SingleOrDefaultAsync(a => a.Slug == normalized, ct);
    if (row?.OwnerId is not { } ownerId || !await IsVisibleAsync(ownerId, ct)) return null;
    var current = await db.UserPublicAddresses.AsNoTracking()
        .SingleOrDefaultAsync(a => a.OwnerId == ownerId && a.State == PublicAddressState.Canonical, ct);
    return current is null ? null
        : new PublicAddressMetadata(ownerId, current.Slug, $"/users/{current.Slug}", current.Revision, normalized != current.Slug);
}

private Task<bool> IsVisibleAsync(Guid id, CancellationToken ct) =>
    db.Users.AsNoTracking().AnyAsync(u => u.Id == id && !u.IsSoftDelete && !u.IsBlocked, ct);
```

### GUID route privacy fix (Open Question 3, body-only, no codegen)
```csharp
// GetPublicUserProfileHandler.cs:16-21. The signature is unchanged, so the generated
// GetPublicUserProfileCommandHandler1926808727.cs (which calls handler.Handle(cmd, userRepository, _mapper, ct)) stays valid.
var user = await userRepository.GetById(command.UserId, ct);
if (user is null || user.IsSoftDelete || user.IsBlocked)
    throw new NotFoundException("User not found");
```

### Gateway route (Pitfall 4)
```json
"account-Users-by-slug-route": {
  "ClusterId": "account-cluster",
  "Match": { "Path": "/api/Users/by-slug/{slug}" },
  "RateLimiterPolicy": "auth-endpoints"
}
```

## State of the Art

| Old Approach | Current Approach | Impact |
|--------------|------------------|--------|
| Profile reachable only by GUID; deleted and blocked profiles returned | Readable `/users/{slug}` with a visibility rule; GUID route aligned (Open Question 3) | The privacy rule is enforced on every public read path |
| Four-kind switch coordinator (Shops) | Single-kind Account-local registry | No kind dispatch, so there is no default-to-wrong-kind risk (phase 3 Pitfall 2 does not apply) |

**Deprecated/outdated:** `CachedUserRepository` is dead code (unregistered). Leave it; do not wire it in this phase.

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | PostgreSQL advisory locks are scoped per database, so the Shops lock key could be reused in `cpaccountdb` | Pattern 2 | None if the recommended distinct key is used |
| A2 | The external frontend will mount profiles at `/users/{slug}` | Open Question 7 | Low: `canonicalPath` is one server literal; change it before any backfill |
| A3 | The frontend links review authors via one batch call per page, and does not need slugs inside Shops DTOs | Q6 | Medium: if false, a Contract event plus a Shops denormalization would be needed later (additive) |
| A4 | Username-change volume is low, so serializing registrations and username changes on one advisory lock is acceptable | Pattern 2 | Low: the lock is held only within those transactions |
| A5 | An SSR frontend calling from one server IP would hit the 20/min `auth-endpoints` limit | Open Question 6 | Medium for phase 6: the existing GUID profile route has the same limit today |

## Open Questions (RESOLVED — see 04-CONTEXT.md: Q1→D-01/D-02, Q2→D-03, Q3→D-04, Q4→D-05, Q5–Q9→D-06..D-10)

These are needed before planning. Each has a recommended option. Questions 1–4 change plan content; 5–9 default to the recommendation if unanswered.

1. **Username → slug policy.**
   - **Recommended: A, follow username, keep aliases.** Allocate at creation from the username. A username change is an owner-authorized rename: old slug → alias, A→B→A restores, case- or punctuation-only changes are no-ops.
   - B: a stable slug that never follows the username. This is the smallest diff, but an old or email-derived name stays canonical forever.
   - C: follow the username but tombstone old slugs. Most private, but old links 404 and it contradicts the spec's alias retention.

   **1b. Squatting under A.** Every username change permanently reserves one slug, with no cooldown today. That is up to 20 changes/min per IP, bounded only by `auth-endpoints`. **Recommended:** document this as an enablement-gate (phase 5) item and do not add a cooldown now. The alternative is a small domain cooldown (a nullable `UsernameChangedAtUtc` column in the same migration, checked in `User.UpdateUsername`; no handler signature change).

2. **Profile visibility rule.**
   - **Recommended:** public iff `!IsSoftDelete && !IsBlocked`. Blocked users are hidden without retirement; unconfirmed-email users stay visible, because an email change resets confirmation (`UserCredential.cs:41-54`) and hiding would flap.
   - Alternatives: also require `EmailConfirmed`; or add an opt-out privacy flag (new column, endpoint, handler and codegen). Defer the flag.

3. **Fix the existing GUID leak** (`GET api/Users/{id}` returns deleted and blocked users). **Recommended: yes.** It is a body-only change, returns 404 for deleted and blocked users, leaves the DTO unchanged, needs no codegen, and makes the by-slug data read re-check visibility for free. Without it, "deleted profiles never leak" is false, because review author GUIDs are public.

4. **Reuse strategy.** **Recommended:** an Account-local single-kind copy (no Shops edits), with schema model tests pinning parity. The alternative is to move `RunAsync` and `Configure<T>` into `CoffeePeek.Shared.Persistence` and refactor four Shops files.

5. **Audit and history.** **Recommended:** no Account audit table and no admin history endpoint. The registry rows are the history, and nothing links a deleted user to former slugs. The alternative is an Account-local audit (no `Kind`), with all rows for the owner deleted in the same retirement transaction.

6. **Gateway routing and rate limit.** **Recommended:** a dedicated `/api/Users/by-slug/{slug}` route on `auth-endpoints` (fixes Pitfall 4, keeps enumeration parity). Batch and legacy-GUID stay on the existing catch-all. Revisit the limits in phase 6 if the frontend uses SSR. The alternative is to reserve `reviews` in `PublicSlug` (a shared rule change).

7. **Canonical path.** **Recommended:** `/users/{slug}`. The frontend owns the real page route; the server literal must be final before the first backfill.

8. **Google email-derived usernames become crawlable URLs.** **Recommended:** accept (the username is already public), and document in the frontend handoff that the frontend may `noindex` profiles. The alternative, not allocating until the user picks a username, needs a new "username chosen" flag; defer it.

9. **Admin scope.** **Recommended:** backfill only. Admin initialize, rename or history for users is not needed under Policy A.

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| .NET SDK | build/test | ✓ | 10.0.400 | — |
| dotnet-ef | migration | ✓ | 10.0.5 | — |
| make | `make mig-acc` | ✓ | GNU Make 4.4.1 | manual `dotnet ef migrations add ... --project CoffeePeek.Account.Persistence/CoffeePeek.Account.Persistence.csproj --startup-project CoffeePeek.AccountService/CoffeePeek.AccountService.csproj --context CoffeePeek.Account.Persistence.Configuration.AccountDbContext --output-dir Migrations` |
| Docker daemon | live PG gate | ✗ (`//./pipe/dockerDesktopLinuxEngine` missing; CLI 28.5.2 present) | — | None. The live gate stays open and the feature stays disabled. When Docker is available: `docker compose -f dev/docker-compose.dev.yml up -d` + `dev/migrate-and-seed.sh` (AGENTS.md:17-28) |
| psql | live PG gate | ✗ | — | None |

**Missing with no fallback:** live PostgreSQL. It blocks only enablement, not implementation.

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit v3 3.2.2 + FluentAssertions 8.9.0 + Moq 4.20.72; EF InMemory 10.0.5 for behavior; Npgsql design-time model (`UseNpgsql("Host=localhost;Database=unused;...")`, no connection) for schema assertions, same as `PublicAddressPersistenceTests.cs:57-58` |
| Config file | none (csproj `<Using Include="Xunit"/>`); all Account test projects are already in `CoffeePeek.Backend.ci.slnf` |
| Quick run command | `dotnet test CoffeePeek.Account.Infrastructure.Tests/CoffeePeek.Account.Infrastructure.Tests.csproj --filter "FullyQualifiedName~PublicAddress"` |
| Full suite command | `dotnet test CoffeePeek.Account.Domain.Tests` (baseline **231**), `dotnet test CoffeePeek.Account.Application.Tests` (baseline **214**), `dotnet test CoffeePeek.Account.Infrastructure.Tests` (baseline **54**, includes the Wolverine codegen guard), `dotnet test CoffeePeek.Gateway.Tests` (baseline **54**), then `dotnet build CoffeePeek.Backend.ci.slnf -c Release` [all baselines VERIFIED this session at `96acf741`] |
| Schema check | `PostgresCpOptions__ConnectionString="Host=localhost;Database=unused;Username=u;Password=p" dotnet ef migrations has-pending-model-changes --project CoffeePeek.Account.Persistence/CoffeePeek.Account.Persistence.csproj --startup-project CoffeePeek.AccountService/CoffeePeek.AccountService.csproj --context CoffeePeek.Account.Persistence.Configuration.AccountDbContext` (baseline clean) plus `dotnet ef migrations script 20260921104154_AddAccountDeletionConfirmationSecurity <new> ...` for SQL review |
| Isolation check | `git diff --quiet 96acf741 -- CoffeePeek.Shops.* CoffeePeek.ShopsService CoffeePeek.Shared.Domain CoffeePeek.Contract CoffeePeek.AccountService/Internal/Generated` must exit 0 |

### Behavior → Test Map
| Label | Behavior | Test Type | Where | Exists? |
|--------|----------|-----------|-------|---------|
| P4-01 | Register (enabled) `Ivan.Petrov` → `ivan-petrov` rev 1; resolve `IVAN-PETROV` is canonical, not alias; `canonicalPath` `/users/ivan-petrov` | InMemory | `UserPublicAddressServiceTests.cs` | ❌ Wave 0 |
| P4-01 | `Ivan` then `ivan` then `Иван` → `ivan`, `ivan-<8hex>`, `ivan-<8hex>` (distinct); `admin`/`日本語` → `user-<8hex>` | InMemory | same | ❌ |
| P4-01 | `User.CreateExternal` (Google path) added → allocated | InMemory | same | ❌ |
| P4-02 | Username A→B→A: B canonical, A alias → A restored; same row; revisions 1→2→3 | InMemory | same | ❌ |
| P4-02 | Case-only (`Ivan`→`ivan`) and punctuation-only (`a.b`→`a_b`) changes → no revision bump; suffixed owner keeps its own suffix | InMemory | same | ❌ |
| P4-02 | New username whose slug is another user's alias or tombstone → suffix | InMemory | same | ❌ |
| P4-02/P4-06 | About update, stats increment, block/unblock → zero new rows, no revision change (Pitfall 1) | InMemory | same | ❌ |
| P4-03 | Blocked → resolve null, batch empty, `{id}` 404; unblock → same slug resolves | InMemory | same | ❌ |
| P4-03 | `GetPublicUserProfileHandler` → `NotFoundException` for soft-deleted and blocked (Open Question 3) | unit (Moq) | `GetProfileHandlerTests.cs` (extend `GetPublicUserProfileHandlerTests`) | ✅ extend |
| P4-03 | by-slug controller: unknown → 404; resolved but profile `NotFoundException` → 404 with empty body; happy path envelope uses `address.EntityId` | controller + fakes | `PublicUserAddressControllerTests.cs` | ❌ |
| P4-04 | Confirm-deletion path (`SetSoftDelete` + `Update` + save) → every row a tombstone, no row with `OwnerId == userId`, resolve of the old canonical and aliases → null; works with **Enabled=false** | InMemory | `UserPublicAddressServiceTests.cs` | ❌ |
| P4-04 | Soft-deleted user whose username is rewritten → no new allocation | InMemory | same | ❌ |
| P4-04 | New user with a deleted user's username → suffixed slug (tombstone reserved) | InMemory | same | ❌ |
| P4-05 | Backfill: dry-run writes nothing; cursor; replay idempotent; excludes soft-deleted; includes blocked | InMemory | same | ❌ |
| P4-06 | Disabled: registration/rename → zero rows; service methods → 503 | InMemory | same | ❌ |
| schema | `UserPublicAddresses`: table, 7 properties only, no navigation, `C` collation, max 100, concurrency token, unique unfiltered Slug, filtered canonical index `"State" = 1`, 4 checks, FK → `User` Restrict | Npgsql model | `UserPublicAddressPersistenceTests.cs` | ❌ |
| domain | `UserPublicAddress` lifecycle (create/alias/restore/retire, invalid slug, UTC) | unit | `Account.Domain.Tests/UserPublicAddressTests.cs` | ❌ |
| API | Routes `api/Users` + `by-slug/{slug}`, `public-addresses`, `{id:guid}/public-address`; NoStore; no `[Authorize]` on public; admin `api/admin/users/public-addresses` + `backfill` with Admin policy and no `[AllowAnonymous]`; `UsersController` templates unchanged | reflection | `PublicUserAddressControllerTests.cs` | ❌ |
| P4-07 | Gateway: `account-Users-by-slug-route` → `account-cluster`, path `/api/Users/by-slug/{slug}`, `auth-endpoints` | JSON config | `ReverseProxyRouteConfigurationTests.cs` | ✅ extend |
| codegen | No handler drift | host start | `WolverinePreGeneratedHandlersTests.cs` (must stay green) | ✅ |

### What cannot be proven without live PostgreSQL (add to the `docs/api/public-addresses.md` gate)
- The unique `Slug` and filtered canonical owner index on `UserPublicAddresses` under concurrent registrations (`Ivan`/`ivan`/`Иван` at once → distinct slugs, serialized by the advisory lock).
- Demotion-flush ordering: a rename never violates `IX_UserPublicAddresses_OwnerId` (filtered).
- `FK_UserPublicAddresses_Users_OwnerId` RESTRICT: a hard delete without retirement fails. This only matters for future physical deletion.
- `CK_UserPublicAddresses_Lifecycle` / `_Slug` / `_Revision` / `_Timestamps` reject invalid rows.
- Savepoint rollback inside Wolverine's ambient transaction:
  - A username unique violation leaves the address unchanged.
  - A duplicate-email registration leaves no address row.
  - The Google concurrent-creation retry still succeeds after `ChangeTracker.Clear()`.
- The migration applies cleanly to `cpaccountdb`.

### Sampling Rate
- **Per task commit:** quick run plus the controller/reflection tests.
- **Per wave merge:** full suite + CI slnf Release build + `has-pending-model-changes` + isolation diff.
- **Phase gate:** all green; live-PG items listed as open; `PublicAddresses__Enabled` stays unset and default false for AccountService.

### Wave 0 Gaps
- [ ] `CoffeePeek.Account.Infrastructure.Tests/Persistence/UserPublicAddressServiceTests.cs` (InMemory harness: `new AccountDbContext(options, Options.Create(new PublicAddressOptions { Enabled = ... }))`, `Role.Create("User")` as in `AdminUserQueryRepositoryTests.cs:12-24`)
- [ ] `CoffeePeek.Account.Infrastructure.Tests/Persistence/UserPublicAddressPersistenceTests.cs` (Npgsql model)
- [ ] `CoffeePeek.Account.Infrastructure.Tests/PublicUserAddressControllerTests.cs` (reflection + hand-written fakes, as in `PublicCatalogAddressControllerTests.cs`)
- [ ] `CoffeePeek.Account.Domain.Tests/UserPublicAddressTests.cs`
- No framework installation is needed.

## Security Domain

### Applicable ASVS Categories
| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | no (public reads; existing JWT for admin) | — |
| V3 Session Management | no | — |
| V4 Access Control | yes | Public lookups anonymous (same as the existing GUID route). Admin backfill: gateway `account-admin-route` Admin **and** `[Authorize(Policy = RoleConsts.Admin)]` on the controller |
| V5 Input Validation | yes | `PublicSlug.IsValid` (strict `\A…\z`, reserved, GUID-shaped rejected), lowercase normalization, batch 1–100 non-empty GUIDs, backfill limit 1–100 |
| V6 Cryptography | no | — |
| V8 Data Protection | yes | Payload excludes email/phone/roles/sessions; deleted and blocked users give an identical 404 on every path (by-slug, batch, `{id}`, and the GUID profile route via Open Question 3); tombstones have `OwnerId = NULL` and no audit; NoStore |

### Known Threat Patterns
| Pattern | STRIDE | Mitigation |
|---------|--------|------------|
| Username existence probing via by-slug | Information disclosure | Parity with the existing registration oracle; `auth-endpoints` 20/min per IP (right-most XFF, `RateLimitingExtensions.cs:45-61`); identical 404s |
| Deleted or blocked profile disclosure | Information disclosure | Visibility filter in the service, plus a re-check in `GetPublicUserProfileHandler` (Open Question 3) |
| Deleted-user linkage via registry or audit | Information disclosure | Retire all rows (`OwnerId = NULL`); no audit table; model test pins the column set |
| Old→new username linkage via alias | Information disclosure | Inherent to Policy A; document it; Policy C avoids it |
| Slug squatting by repeated username changes | DoS / Tampering | Rate limit; document as an enablement-gate item; optional cooldown (Open Question 1b) |
| Homoglyph or transliteration impersonation (`Иван` vs `ivan`) | Spoofing | First-come canonical, deterministic suffix for others; the display username is still shown in `data` |
| Open redirect via `canonicalPath` | Tampering | Server-built `/users/` + stored, validated slug; never echo request input |
| Email local-part exposure via crawlable Google-derived slugs | Information disclosure | Open Question 8 (document; frontend `noindex` option) |
| Gateway misrouting (`reviews`) | DoS | Dedicated by-slug gateway route plus a config test |

## Sources

### Primary (HIGH confidence)
- Codebase (file:line cited throughout):
  - Account: `User.cs`, `Username.cs`, `UserCredential.cs`, `UserConfiguration.cs`, `AccountDbContext.cs`, `AccountDbContextFactory.cs`, `Account.Persistence/DependencyInjection.cs`, `UserRepository.cs`, `CachedUserRepository.cs`, `QueryUserRepository.cs`, `UsersController.cs`, `AdminUsersController.cs`, `GetPublicUserProfileHandler.cs`, `PublicUserProfileResponse.cs`, `MapsterConfiguration.cs`, `UpdateProfileUsernameHandler.cs`, `RegisterUserHandler.cs`, `ExternalAuthService.cs`, `OAuthLoginHandler.cs`, `AuthService.cs`, `ConfirmAccountDeletionHandler.cs`, `AdminDeleteUserCommand.cs`, `BlockUserCommand.cs`, `AccountDeletionRequest.cs`, `ReviewAddedHandler.cs`, the generated `RegisterUserCommandHandler748332044.cs` / `GetPublicUserProfileCommandHandler1926808727.cs` / `ConfirmAccountDeletionCommandHandler1106701760.cs`, and `AccountDbContextModelSnapshot.cs`.
  - Shops and shared: `ShopsDbContext.cs`, `PublicAddressService.cs`, `PublicAddressWriter.cs`, `PublicAddressTransaction.cs`, `PublicAddressConfiguration.cs`, `IPublicAddressService.cs`, `PublicSlug.cs`, `PublicAddress.cs`, `PublicAddressDtos.cs`, `ReviewDto.cs`, `UserReviewsController.cs`, `UserNameChangedEventHandler.cs`, `WolverineModule.cs`, `UnitOfWork.cs`, `TransientDbConflict.cs`, `GlobalExceptionHandler.cs`.
  - Gateway and deploy: `CoffeePeek.Gateway/appsettings.json`, `RateLimitingExtensions.cs`, `AuthExtensions.cs`, `CoffeePeek.Backend.ci.slnf`, `deploy/docker-compose.yml`, `deploy/scripts/apply-migrations.sh`.
- Planning: `.planning/specs/PUBLIC-SLUGS.md`, `.planning/STATE.md`, `.planning/ROADMAP.md`, phase 2/3 SUMMARY, RESEARCH, REVIEW and VERIFICATION, `docs/api/public-addresses.md`, `AGENTS.md`, quick task `260921-account-deletion-workflow/260921-SUMMARY.md`.
- Session spikes (throwaway, outside the repo, deleted):
  1. `PublicSlug.Generate/Candidate` on 32 usernames.
  2. EF Core 10.0.5 `AddDbContextPool` with an optional `IOptions<T>` ctor parameter: resolved and injected.
  3. The real `AccountDbContext` + `UserRepository` on InMemory: `DbSet.Update` marks all properties modified and preserves original values.

  Also run: the `has-pending-model-changes` baseline (clean) and the test baselines (231/214/54/54).
- Context7 `/microsoft/aspire.dev`: `AddNpgsqlDbContext` "Enables db context pooling". Context7 `/dotnet/yarp`: routes are ASP.NET Core endpoints, lower `Order` wins, more specific routes match first.
- learn.microsoft.com/aspnet/core/fundamentals/routing (aspnetcore-10.0), "Route template precedence": literal > constrained parameter > parameter > catch-all.

### Secondary / Tertiary
- None.

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH (no new packages; versions read from props).
- Current surface, lifecycle and codegen coupling: HIGH (every path traced to file:line; change detection and pooling verified by spike).
- Architecture (hook, single-kind registry): HIGH for mechanics. MEDIUM for the demotion-flush ordering and rollback, which need the live PG gate.
- Policy recommendations (username policy, visibility, GUID fix, audit, gateway): MEDIUM, because they are user decisions.
- Pitfalls: HIGH (IsModified, gateway capture, pooling and migration environment all verified this session).

**Research date:** 2026-09-28
**Valid until:** 2026-10-28 (stable internal codebase; re-check if `UsersController`, `AccountDbContext`, gateway routes or the account-deletion flow change)
