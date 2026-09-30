---
phase: 3
slug: cities-zones-and-catalog-filters
status: draft
nyquist_compliant: true
wave_0_complete: false
created: 2026-09-28
---

# Phase 3 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution. Details: `03-RESEARCH.md` § Validation Architecture.

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit v3 + FluentAssertions + Moq; EF InMemory; Npgsql design-time model for schema assertions |
| **Config file** | none |
| **Quick run command** | `dotnet test CoffeePeek.Shops.Infrastructure.Tests --filter "FullyQualifiedName~PublicAddress"` + `dotnet test CoffeePeek.ShopsService.Tests` |
| **Full suite command** | `dotnet test` on Shops.Domain.Tests, Shops.Application.Tests, Shops.Infrastructure.Tests, ShopsService.Tests; `dotnet build CoffeePeek.Backend.ci.slnf -c Debug --no-restore`; `has-pending-model-changes` |
| **Estimated runtime** | ~120 seconds |

## Sampling Rate

- **After every task commit:** quick run command
- **After every plan wave:** full suite command
- **Before verify:** full suite green
- **Max feedback latency:** 120 seconds

## Per-Task Verification Map

| Task | Behaviors | Automated verify | Test files |
|------|-----------|------------------|------------|
| 03-01 T1 | schema, D-01, D-03 | persistence + domain PublicAddresses tests; `has-pending-model-changes`; migration script grep | `PublicAddressPersistenceTests.cs`, `PublicAddressTests.cs`, `PublicSlugTests.cs` |
| 03-01 T2 | P3-01, P3-02, P3-03, P3-04, P3-06, D-01, D-02 | `dotnet test CoffeePeek.Shops.Infrastructure.Tests --filter FullyQualifiedName~PublicAddress` + full Infrastructure/Application suites | `CityZonePublicAddressServiceTests.cs` (new), `PublicAddressServiceTests.cs` (unchanged) |
| 03-02 T1 | P3-03, P3-04, D-02 (controller re-check) | `dotnet test CoffeePeek.ShopsService.Tests` | `PublicAddressApiContractTests.cs`, `PublicCatalogAddressControllerTests.cs` (new), `WolverinePreGeneratedHandlersTests.cs` |
| 03-02 T2 | P3-05, P3-06, D-04, D-05 | CI slnf restore + Release build, ShopsService.Tests Release, full Shops suites, drift check, unchanged-path git diff, doc greps | — |

## Wave 0 Requirements

- [ ] New InMemory city/zone lifecycle tests (tracked-instance zone pattern)
- [ ] New `[InlineData]` rows + audit Kind check fact in `PublicAddressPersistenceTests.cs`
- [ ] Contract tests for new controllers, admin regex and Kind mapping

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Unique/filtered indexes under concurrency, RESTRICT FKs, rollback on city delete with zones, audit Kind 3/4 check, advisory lock | P3-06 | Live PostgreSQL unavailable (Docker daemon down) | Live gate in `docs/api/public-addresses.md` |

## Validation Sign-Off

- [x] All tasks have automated verify
- [x] No watch-mode flags
- [x] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
