---
phase: 04-public-profiles-in-account
plan: 03
subsystem: gateway
tags: [slugs, public-addresses, gateway, yarp, routing, docs, rollout, user-profiles]

requires:
  - phase: 04-public-profiles-in-account
    provides: "Plan 04-01: migration 20260928134755_AddUserPublicAddresses, advisory lock 7402100401, user-<8hex> fallback. Plan 04-02: api/Users by-slug/batch/GUID endpoints, backfill-only admin API, D-04 GUID-route 404"
provides:
  - "Gateway route account-Users-by-slug-route (/api/Users/by-slug/{slug} -> account-cluster, auth-endpoints, anonymous)"
  - "Config and ASP.NET Core template-precedence tests proving the reviews slug resolves in Account"
  - "docs/api/public-addresses.md user profile contract, username/privacy policy, deployment prerequisite, per-service enablement gate with pending Account live PostgreSQL checks and the D-02 squatting decision"
affects: [frontend-phase-6-profile-urls, live-postgresql-gate, account-enablement, shops-account-user-existence-lookup]

tech-stack:
  added: []
  patterns:
    - "Gateway route precedence proven with Microsoft.AspNetCore.Routing.Template RoutePrecedence.ComputeInbound + TemplateMatcher.TryMatch, with a non-vacuity check that every competing template matches the contested path"

key-files:
  created:
    - .planning/phases/04-public-profiles-in-account/04-03-SUMMARY.md
  modified:
    - CoffeePeek.Gateway/appsettings.json
    - CoffeePeek.Gateway.Tests/ReverseProxyRouteConfigurationTests.cs
    - docs/api/public-addresses.md

key-decisions:
  - "The dedicated by-slug gateway route fixes the reviews-slug capture instead of reserving reviews in the shared PublicSlug list (research Open Question 6)"
  - "The public-address flag is per service: AccountService is enabled separately from ShopsService, and only after its own live gate and the D-02 squatting decision"
  - "The user profile docs also list an HTTP/gateway smoke run as pending, because route order is proven only through the ASP.NET Core routing precedence API"

patterns-established:
  - "Route precedence tests: parse Match.Path from appsettings, assert all contested templates match the path (non-vacuous), then compare ComputeInbound values"

requirements-completed: [D-01, D-02, D-03, D-04, D-06, D-07, D-08, D-09, D-10, P4-06, P4-07]

duration: 6min
completed: 2026-09-29
---

# Phase 4 Plan 03: Gateway By-Slug Route, User Profile Docs and Phase Verification Summary

**A dedicated `account-Users-by-slug-route` sends `/api/Users/by-slug/{slug}` to AccountService on the `auth-endpoints` limit, with no auth policy. A template-precedence test proves it outranks the Shops `/api/users/{userId}/reviews` route for the slug `reviews`. The rollout doc now covers the user profile contract, username and privacy policy, the flag-independent Account migration prerequisite and the still-pending Account live PostgreSQL gate.**

## Performance

- **Duration:** about 6 min
- **Started:** 2026-09-29T07:10:52Z
- **Completed:** 2026-09-29T07:17:08Z
- **Tasks:** 2 of 2 (Task 1 TDD: RED + GREEN; Task 2 docs + phase gate)
- **Files modified:** 3

## Accomplishments

- **Gateway route (D-07).** A new `account-Users-by-slug-route` sits immediately before `account-Users-route`:
  - `ClusterId` is `account-cluster`, `Match.Path` is `/api/Users/by-slug/{slug}` and `RateLimiterPolicy` is `auth-endpoints`.
  - It has no `AuthorizationPolicy` and no `Methods`.
  - The diff against `96acf741` is 5 added lines; no existing line was removed or changed.
  - Batch and legacy GUID lookups stay on the existing catch-all.
- **Tests.**
  - `UserBySlugRoute_TargetsAccountOnAuthRateLimitWithoutAuthPolicy` checks the route shape, that it has no auth policy or methods, and that it comes before the Users catch-all.
  - `UserBySlugRoute_OutranksShopsUserReviewsForEverySlug` first checks that `/api/Users/by-slug/reviews` matches all three templates (by-slug, Shops user reviews, Users catch-all), so the risk is real. It then checks that by-slug has the lowest `RoutePrecedence.ComputeInbound`, that `ivan-petrov` matches, and that a real Shops user-review path does not match by-slug.
- **Docs (`docs/api/public-addresses.md`).**
  - **Intro:** adds user profiles, plus the flag-independent `GET api/Users/{id}` exception. The "has not been run against production data" sentence is kept.
  - **Deployment prerequisite:** `20260928134755_AddUserPublicAddresses` must be applied to `cpaccountdb` before this AccountService build serves traffic, flag on or off, because deletion always retires. `apply-migrations.sh` migrates Account first. This extends the existing Phase 3 script rules (`SKIP_MIGRATIONS`, `update.sh`) instead of repeating them.
  - **Per-service flag:** both services are currently unset (false).
  - **Pending Account live PostgreSQL checks**, with an explicit note that none was verified:
    - unique `Slug` and `IX_UserPublicAddresses_OwnerId` under concurrent `Ivan`/`ivan`/`Иван` registrations, with lock `7402100401`;
    - the demotion flush;
    - FK RESTRICT;
    - the four checks;
    - the savepoint rollback cases;
    - backfill cursor and 409 handling;
    - the migration applying cleanly;
    - an HTTP/gateway smoke run.
  - **D-02 gate step:** accept slug reservation growth or add a cooldown first. No cooldown exists in this build.
  - **Public API:** a user profile table row, an alias JSON example (`ivan-petrov` resolving to `/users/petr`, `isAlias: true`, revision 2 as the writer produces after a rename), the gateway routing note, and a no-cache note.
  - **New `## User profiles` section:** D-01 username policy, D-03 visibility with the identical 404 and generic ProblemDetails body (no user data), the D-04 GUID fix and its consequence for Shops `AccountUserExistenceLookup`, deletion and tombstones (D-06), disclosure and `noindex` (D-09), and author links through the batch endpoint.
  - **Admin API:** the backfill-only user endpoint (D-10).
  - **Bounded backfill:** user backfill semantics, including exactly what `hadCollision` means.
  - **Rollback:** the `Down` warning and the per-service rollback note.

## Task Commits

1. **Task 1: Dedicated gateway by-slug route with config and template-precedence tests**
   - RED `ec26f930`: `test(04-03): add failing gateway by-slug route and precedence tests`. The project compiled, so `RoutePrecedence` and `TemplateMatcher` are available; 2 tests failed and 54 passed.
   - GREEN `05de23df`: `feat(04-03): route /api/Users/by-slug/{slug} to AccountService on auth-endpoints`
2. **Task 2: API/rollout documentation and full phase verification**: `85f5b428` `docs(04-03): document user profile addresses, deployment prerequisite and Account live gate`

No refactor commit was needed. No packages were added and no tracked file was deleted.

## Phase Verification (CI configuration)

| Check | Result |
|---|---|
| `dotnet restore` + `dotnet build CoffeePeek.Backend.ci.slnf -c Release` | 0 errors (470 warnings, all pre-existing; none from `ReverseProxyRouteConfigurationTests.cs`) |
| `CoffeePeek.Account.Domain.Tests` (Release) | 255 passed, 0 failed (baseline 231) |
| `CoffeePeek.Account.Application.Tests` (Release) | 216 passed, 0 failed (baseline 214) |
| `CoffeePeek.Account.Infrastructure.Tests` (Release) | 89 passed, 0 failed (baseline 54), including the Wolverine pre-generated handler guard |
| `CoffeePeek.Gateway.Tests` (Release) | 56 passed, 0 failed (baseline 54) |
| `has-pending-model-changes` (Account) | "No changes have been made to the model since the last migration." |
| `git diff --check` | clean |
| Isolation diff vs `96acf741` (Shops.*, ShopsService, Shared.Domain/Persistence/Kernel, Contract, AccountService/Internal/Generated, UsersController, AdminUsersController, PublicUserProfileResponse, ci-cd.yml, ci.slnf) | `git diff --quiet` exit 0 |
| Flag grep `PublicAddresses(__\|:\|")` in AccountService/ShopsService appsettings, `deploy/`, `CoffePeek.AppHost/` | no matches (flag unset, default false) |
| `UsernameChangedAtUtc` in Account.Domain/Persistence | no matches (D-02: no cooldown) |
| Gateway appsettings diff vs `96acf741` removed lines | 0 |
| Required doc strings (20 items from the acceptance criteria) | all present; no "empty" 404 wording |

**Not verified:** there was no live PostgreSQL, Docker, HTTP or running YARP gateway. Route precedence is proven only through ASP.NET Core's routing precedence API (`RoutePrecedence`, `TemplateMatcher`); YARP was not run. The Account live gate stays open, and the doc lists every item as pending:
- concurrent slug uniqueness and the filtered owner index (lock `7402100401`);
- demotion-flush ordering;
- `FK_UserPublicAddresses_Users_OwnerId` RESTRICT;
- `CK_UserPublicAddresses_*`;
- savepoint rollback (username conflict, duplicate email, Google retry after `ChangeTracker.Clear()`);
- the backfill cursor and concurrent 409;
- the migration applying to `cpaccountdb`;
- an HTTP smoke run through the gateway (`/api/Users/by-slug/reviews`, `ids=` binding, identical 404 body).

`PublicAddresses__Enabled` stays unset for AccountService and ShopsService.

## Decisions Made

- The route fix is config-only, and `PublicSlug.Reserved` is unchanged (research Open Question 6).
- The flag is enabled per service. AccountService gets its own gate steps: step 5 (the D-02 decision) and step 6 (enable, then run the user backfill).
- The HTTP/gateway smoke run is listed as a pending gate item. This makes explicit that the precedence test is not runtime proof.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] The docs commit picked up a `.planning/STATE.md` change staged by someone else**
- **Found during:** Task 2 commit
- **Issue:** `.planning/STATE.md` was not staged when Task 1 was committed. By the time of the docs commit, something outside this session had staged it (`M ` in the index), and the plain `git commit` included it in the first docs commit `f1f7973a`. That violated commit_docs=false.
- **Fix:** the commit was mine and not pushed, so I ran `git reset --soft HEAD~1` and recommitted with `git commit --only docs/api/public-addresses.md` as `85f5b428`, which contains only the doc. The external staged STATE.md change is still staged as I found it. It was not committed and not discarded.
- **Files modified:** none beyond the plan
- **Commit:** `85f5b428` (replaces the unpushed `f1f7973a`)

### Additions within plan scope (no behavior impact)

- The config test also asserts that the route has no `Match.Methods`, which the plan's action already forbids.
- The doc adds these beyond the listed bullets:
  - The intro calls out the flag-independent D-04 exception.
  - A pending HTTP/gateway smoke-run gate item and the backfill concurrent-409 gate item.
  - A note that a username change made while the flag is off leaves an existing address on the old slug.
  - A 400/503 note for user endpoints.
  - A note that users are a fifth independent namespace.
  - The already-initialized case of `hadCollision`.
  - A per-service rollback note.
  All of these were checked against the implementation: `ProfileAddressWriter`, `ProfileAddressService`, `PublicSlug`, `apply-migrations.sh`.
- The User profiles section is placed after "Cities and coffee zones" to follow the order in the intro. The Cities text is unchanged.

## Issues Encountered

- The staged-file incident above.
- `gsd-sdk query roadmap.update-plan-progress 4 04-03 complete` checked 04-02 and 04-03 and marked the phase-4 line `[x]`. It inserted " (completed 2026-09-29)" after the line's CR, which left a bare CR in the CRLF file. I fixed that line by hand and also changed the stale Progress row from `4–6 Not started` to `4 | 3/3 implemented | PostgreSQL verification pending; feature default-off`, matching phase 3. I kept the SDK's phase-level `[x]`, although phases 1–3 remain `[ ]` while their live gates are open. The orchestrator or phase verification should decide which convention applies.

## Known Stubs

None.

## Threat Flags

None. The only new surface is the anonymous by-slug gateway route, which T-04-16 to T-04-18 cover. The config test asserts there is no auth policy and that the `auth-endpoints` limiter is applied.

## Next Phase Readiness

- Phase 4 is complete in code and docs. Enabling AccountService requires the deployment prerequisite, the pending Account live PostgreSQL gate and the D-02 decision.
- Frontend (phase 6): render `/users/{slug}` through `GET api/Users/by-slug/{slug}`, 301 aliases and GUID pages to `address.canonicalPath`, and link review authors through `GET api/Users/public-addresses?ids=`. Revisit the `auth-endpoints` 20/min limit if pages are rendered server-side.

## Self-Check: PASSED

- `CoffeePeek.Gateway/appsettings.json`, `CoffeePeek.Gateway.Tests/ReverseProxyRouteConfigurationTests.cs`, `docs/api/public-addresses.md` and this SUMMARY exist on disk.
- Commits `ec26f930`, `05de23df` and `85f5b428` are present in `git log`.
