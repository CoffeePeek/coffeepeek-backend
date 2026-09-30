---
phase: 4
slug: public-profiles-in-account
status: draft
nyquist_compliant: true
wave_0_complete: false
created: 2026-09-28
---

# Phase 4 — Validation Strategy

> Per-phase validation contract. Details: `04-RESEARCH.md` § Validation Architecture.

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit v3 + FluentAssertions + Moq; EF InMemory; Npgsql design-time model for schema assertions; ASP.NET Core `RoutePrecedence`/`TemplateMatcher` for gateway precedence |
| **Config file** | none |
| **Quick run command** | `dotnet test CoffeePeek.Account.Infrastructure.Tests --filter "FullyQualifiedName~UserPublicAddress"` |
| **Full suite command** | `dotnet test` on Account.Domain.Tests (231), Account.Application.Tests (214), Account.Infrastructure.Tests (54), Gateway.Tests (54); `dotnet build CoffeePeek.Backend.ci.slnf -c Release --no-restore`; Account `has-pending-model-changes` |
| **Estimated runtime** | ~120 seconds |

## Sampling Rate

- **After every task commit:** quick run command, plus the task's own verify command.
- **After every plan wave:** full suite command.
- **Before verify:** full suite green (plan 04-03 Task 2 is the phase gate).
- **Max feedback latency:** 120 seconds.

## Per-Task Verification Map

| Task | Behaviors | Automated verify | Test files |
|------|-----------|------------------|------------|
| 04-01 T1 | schema parity (D-05), 7 columns and no navigation (D-03), one table only (D-06), username slug regression rows | `dotnet test CoffeePeek.Account.Infrastructure.Tests --filter FullyQualifiedName~UserPublicAddressPersistenceTests`; `dotnet test CoffeePeek.Account.Domain.Tests --filter FullyQualifiedName~UserPublicAddressTests`; `has-pending-model-changes`; migration file and script greps | `UserPublicAddressPersistenceTests.cs` (new), `UserPublicAddressTests.cs` (new) |
| 04-01 T2 | allocate on email/Google creation, suffixes, `user-<8hex>` fallback, follow username, A->B->A, case/punctuation no-op incl. own suffix (D-01); retire always incl. disabled and hard delete, block no-op, deleted-rename no-op, tombstone reserved (D-03); unrelated updates untouched (Pitfall 1); disabled no allocation | `dotnet test CoffeePeek.Account.Infrastructure.Tests --filter FullyQualifiedName~UserPublicAddress` + full Account.Infrastructure (Wolverine codegen guard) and Account.Application; grep gates (no IsModified, no hook re-entry, lock key, flag unset) | `UserPublicAddressLifecycleTests.cs` (new) |
| 04-02 T1 | resolve canonical/alias/case-insensitive, `/users/` path (D-08), unconfirmed email visible, blocked/deleted/leftover-row hidden (D-03), batch order and validation, invalid slug 400, disabled 503, backfill dry-run/cursor/replay, deleted excluded, blocked included (D-10) | `dotnet test CoffeePeek.Account.Infrastructure.Tests --filter FullyQualifiedName~UserPublicAddress` | `UserPublicAddressServiceTests.cs` (new) |
| 04-02 T2 | GUID route 404 for deleted/blocked (D-04); routes, NoStore, anonymous public, Admin-only single backfill action (D-06, D-10); empty 404 on hidden profile, owner-id command, envelope, no email/phone | `dotnet test CoffeePeek.Account.Application.Tests` + `dotnet test CoffeePeek.Account.Infrastructure.Tests`; body-only handler diff gate; generated-code diff gate | `GetProfileHandlerTests.cs` (extended), `PublicUserAddressControllerTests.cs` (new), `WolverinePreGeneratedHandlersTests.cs` (unchanged) |
| 04-03 T1 | gateway by-slug route shape and rate limit, precedence over `shops-users-reviews-route` for `reviews` (D-07) | `dotnet test CoffeePeek.Gateway.Tests` | `ReverseProxyRouteConfigurationTests.cs` (extended) |
| 04-03 T2 | docs (D-01, D-02, D-03, D-04, D-06, D-08, D-09, D-10), deployment prerequisite, live gate; phase gate | CI slnf restore + Release build; four test projects in Release; `has-pending-model-changes`; flag grep; isolation diff; doc greps | — |

## Wave 0 Requirements

Test scaffolds are created test-first inside each task, not in a separate wave:

- [ ] Account InMemory lifecycle tests for user address allocate/rename-follow/alias/retire/block (04-01 T2)
- [ ] Account schema model tests for `UserPublicAddresses` (04-01 T1)
- [ ] Service visibility/backfill tests (04-02 T1)
- [ ] Contract/route tests for by-slug/batch/legacy endpoints and the GUID-leak 404 (04-02 T2); gateway by-slug route tests (04-03 T1)

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Slug uniqueness under concurrent sign-ups, rename demotion ordering, savepoint rollback inside the Wolverine transaction (username conflict, duplicate email, Google retry after `ChangeTracker.Clear()`), FK/check constraints, backfill cursor translation, migration apply | D-01, D-03, D-10 | Live PostgreSQL unavailable (Docker daemon down) | Live gate in `docs/api/public-addresses.md` (extended by 04-03 T2); `PublicAddresses__Enabled` stays unset |
| Real YARP routing of `/api/Users/by-slug/reviews` | D-07 | No running gateway; precedence is proven with the ASP.NET Core routing precedence API | Smoke through the gateway when the stack runs |

## Validation Sign-Off

- [x] All tasks have automated verify
- [x] No watch-mode flags
- [x] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
