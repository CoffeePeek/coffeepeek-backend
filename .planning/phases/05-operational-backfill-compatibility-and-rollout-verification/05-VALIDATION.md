---
phase: 5
slug: operational-backfill-compatibility-and-rollout-verification
status: draft
nyquist_compliant: true
wave_0_complete: false
created: 2026-09-29
---

# Phase 5 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution. Details: `05-RESEARCH.md` § Validation Architecture.

Two layers:
- **Skip path.** It runs everywhere, including CI and this machine without a server. Every live test compiles and reports Skipped, and every other test stays green.
- **Live path.** It runs only with `COFFEEPEEK_LIVE_PG` set. Gate evidence comes only from plan 05-07 (D-04, D-08). The first live execution is the optional, attested harness check in plan 05-08. Development live runs in 05-03..05-06 happen only when `05-08-SUMMARY.md` contains the exact line `Live attestation: non-production confirmed` and the env var is visible. Their output is filtered to `Total: |LIVE-PG |\[FAIL\]` lines. They are recorded in their SUMMARYs, but they are not evidence. Plan 05-01 runs nothing live.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit v3 3.2.2 + FluentAssertions 8.9.0 + Moq 4.20.72; Npgsql 10.0.2 (transitive); EF Core 10 relational (`MigrateAsync`, `IMigrator`, `HasPendingModelChanges`). No new packages (D-01) |
| **Config file** | none. The opt-in comes from env var `COFFEEPEEK_LIVE_PG` (D-02) plus pgpass/PGPASSFILE; `COFFEEPEEK_LIVE_PG_ALLOW_SUPERUSER=1` only for disposable servers (D-03) |
| **Runner on this machine** | Windows Application Control blocks the xUnit v3 apphost. Build `-c Release -p:AllowMissingPrunePackageData=true`, then run `(cd <project>/bin/Release/net10.0 && dotnet exec <project>.dll -noColor [-class …] [-trait "Category=LivePostgres"])`. Summary line: `Total: N, Errors: E, Failed: F, Skipped: S, Not Run: R` |
| **Quick run command** | the touched class via `-class "<FullName>"` with `env -u COFFEEPEEK_LIVE_PG` (skip path), e.g. `-class "CoffeePeek.Shops.Infrastructure.Tests.LivePostgres.ShopsLiveConcurrencyTests"` |
| **Full suite command** | `dotnet build CoffeePeek.Backend.ci.slnf -c Release -p:AllowMissingPrunePackageData=true`; then full in-process runs of `CoffeePeek.Shops.Infrastructure.Tests`, `CoffeePeek.Account.Infrastructure.Tests` and `CoffeePeek.ShopsService.Tests` with the env var unset (`Errors: 0, Failed: 0`) |
| **Live gate command** | `-trait "Category=LivePostgres" -showLiveOutput` on both infrastructure projects with the env var set (plan 05-07 only) |
| **Estimated runtime** | skip path ~60–120 s per project including build; live run ~1–3 min per project over the network (research A8) |

---

## Sampling Rate

- **After every task commit:** the task's `<automated>` command, which covers the touched class on the skip path plus the full project run where listed. If `[ -n "${COFFEEPEEK_LIVE_PG:-}" ]` AND `grep -qx 'Live attestation: non-production confirmed'` succeeds on `05-08-SUMMARY.md`, also run the touched class live as a development check, filtered through `grep -E "Total: |LIVE-PG |\[FAIL\]"` (never echo the value).
- **After every plan wave:** the full suite command (CI slnf Release build; all live tests Skipped, everything else green).
- **Before `/gsd:verify-work`:** the full suite is green, AND plan 05-07 has recorded one live run per project with `Failed: 0, Skipped: 0` whose totals equal the skip-path counts (D-04). Without a server, the phase can be verified only up to "suite implemented, skip path green, gate still open".
- **Max feedback latency:** 120 seconds (skip path).

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| 5-01-01 | 01 | 1 | D-02, D-03 | T-05-01, T-05-03, T-05-06 | Owned-name regex `\A…\z` gates every DROP; maintenance DB must be `postgres`; no Password key; fixed guard messages leak no secret | unit (server-free, runs in CI) | `-class "CoffeePeek.Shops.Infrastructure.Tests.LivePostgres.LivePostgresGuardTests"` → `Failed: 0, Skipped: 0`, ≥ 40 cases | ❌ W0 | ⬜ pending |
| 5-01-02 | 01 | 1 | D-01, D-02, D-03, D-04 | T-05-02, T-05-04, T-05-05, T-05-07 | Superuser refused unless `=1`; drop on success and failure; `lock_timeout=30s`; a set but unusable config fails and is never skipped | skip path (live self-tests first run in 05-08) | `env -u COFFEEPEEK_LIVE_PG … -trait "Category=LivePostgres"` → `Total: 4 … Skipped: 4`; full Shops and Account runs green | ❌ W0 | ⬜ pending |
| 5-02-01 | 02 | 1 | D-05, G-S10b | T-05-08 | In-flight address 23505 during write backfill → 409 with a fixed message; unrelated DB errors not masked | unit (InMemory + throwing interceptor) | `-class "CoffeePeek.Shops.Infrastructure.Tests.Persistence.PublicAddressBackfillConflictTests"` → `Total: 8 … Failed: 0`; full Shops infra + ShopsService.Tests green | ❌ W0 | ⬜ pending |
| 5-02-02 | 02 | 1 | D-02, D-03, D-04, D-06, D-07, D-09, D-10, G-M1, G-M2 | T-05-09..T-05-12 | Runbook shows no host or token; flag stays unset; no deploy/dev/CI change | doc greps | `grep` gates in the 05-02 verify (sections, "No green run has been recorded yet.", flag grep, `git diff --check`) | n/a | ⬜ pending |
| 5-08-01 | 08 | 2 | D-02, D-03, D-04 | T-05-32, T-05-33 | Optional: user attests a non-production server or replies "later"; no secret pasted | **manual** (checkpoint:human-action) | n/a (user resume signal; presence check with `-n` only) | n/a | ⬜ pending |
| 5-08-02 | 08 | 2 | D-02, D-03, D-04 | T-05-32..T-05-35 | Exactly one attestation line; confirmed only with a filtered green harness run (`Total: 4 … Skipped: 0`, PG ≥ 17); harness fixes keep the 05-01 contract and every guard | live harness (attested) + skip path | 05-08 Task 2 verify (attestation line, no secret in SUMMARY, contract members present, guard tests green, skip path all Skipped, Account suite green) | ❌ W0 | ⬜ pending |
| 5-03-01 | 03 | 3 | G-S0, G-S1, G-S2 | T-05-13, T-05-15, T-05-06 | Exact SqlState/ConstraintName with positive controls; hook-bypassing delete hits RESTRICT FK | live integration (skip path in CI) | `-class "…LivePostgres.ShopsLiveSchemaTests"` → all Skipped (≥ 14); full Shops infra green | ❌ W0 | ⬜ pending |
| 5-03-02 | 03 | 3 | G-S3, G-S4, G-S5, G-S6 | T-05-14 | `pg_locks` / `transactionid` barriers before outcomes; no sleeps | live integration | `-class "…LivePostgres.ShopsLiveConcurrencyTests"` → `Total: 5 … Skipped: 5` | ❌ W0 | ⬜ pending |
| 5-03-03 | 03 | 3 | G-S7, G-S8, G-S9 | T-05-15 | Ambient tx usable after a failed save; city-with-zones 409 rolls back retirement | live integration | `-class "…LivePostgres.ShopsLiveTransactionTests"` → `Total: 3 … Skipped: 3`; full Shops infra green | ❌ W0 | ⬜ pending |
| 5-04-01 | 04 | 3 | G-S0 (incremental), D-01, D-02 | T-05-13, T-05-06 | Pre-feature schema has no address tables; seeded data survives upgrade | live integration | `-class "…LivePostgres.ShopsBackfillRehearsalTests"` → `Total: 1 … Skipped: 1` | ❌ W0 | ⬜ pending |
| 5-04-02 | 04 | 3 | G-S10, D-07 | T-05-16, T-05-17, T-05-19 | Written page == reviewed dry run; server-ordered partition; hidden shop allocated but unresolvable | live integration | same class → `Total: 2 … Skipped: 2` | ❌ W0 | ⬜ pending |
| 5-04-03 | 04 | 3 | G-S10b, G-S11, D-05 | T-05-18 | 409 rolls back the whole page; flag-off 503 everywhere; deletion keeps retiring | live integration | same class → `Total: 2 … Skipped: 2`; full Shops infra green; isolation diff empty | ❌ W0 | ⬜ pending |
| 5-05-01 | 05 | 3 | G-A0, G-A1, G-A2, D-01 | T-05-21, T-05-22 | Exact constraint attribution; hard delete (flag off/on) leaves owner-less tombstones | live integration | `-class "CoffeePeek.Account.Infrastructure.Tests.LivePostgres.AccountLiveSchemaTests"` → `Total: 5 … Skipped: 5`; full Account infra green | ❌ W0 | ⬜ pending |
| 5-05-02 | 05 | 3 | G-A3, G-A4, G-A5 | T-05-20, T-05-21 | 2 advisory waiters on `7402100401`; discriminating in-flight savepoint test | live integration | `-class "…LivePostgres.AccountLiveConcurrencyTests"` → `Total: 4 … Skipped: 4` | ❌ W0 | ⬜ pending |
| 5-05-03 | 05 | 3 | G-A6, G-A7 | T-05-20, T-05-23 | Duplicate email leaves no address; Google retry after `ChangeTracker.Clear()`; codegen guard green | live integration | `-class "…LivePostgres.AccountLiveRegistrationTests"` → `Total: 2 … Skipped: 2`; full Account infra green; `WolverinePreGeneratedHandlersTests` green | ❌ W0 | ⬜ pending |
| 5-06-01 | 06 | 3 | G-A0 (incremental), D-01, D-02 | T-05-13, T-05-06 | Pre-feature Account schema lacks the registry; seeded users survive upgrade | live integration | `-class "CoffeePeek.Account.Infrastructure.Tests.LivePostgres.AccountBackfillRehearsalTests"` → `Total: 1 … Skipped: 1` | ❌ W0 | ⬜ pending |
| 5-06-02 | 06 | 3 | G-A8, G-A9, D-07 | T-05-24, T-05-25, T-05-26 | Deleted users never paged; blocked allocated but hidden; 409 rolls back page; flag-off 503; soft delete retires | live integration | same class → `Total: 2 … Skipped: 2`; full Account infra + codegen guard green | ❌ W0 | ⬜ pending |
| 5-07-01 | 07 | 4 | D-04 (readiness) | T-05-29, T-05-31 | Clean committed tree; skip-path counts N_S/N_A; env presence checked with `-n` only; flag unset | build + skip path | 05-07 Task 1 verify (clean tree, both `-trait` skip lines with `Skipped == Total`, flag grep) | n/a | ⬜ pending |
| 5-07-02 | 07 | 4 | D-02, D-03, D-04 | T-05-28, T-05-29 | User attests non-production; no secret pasted | **manual** (checkpoint:human-action) | n/a (user resume signal) | n/a | ⬜ pending |
| 5-07-03 | 07 | 4 | D-04, D-06, D-08, D-09, D-10, all G-S*/G-A* | T-05-27, T-05-29, T-05-31 | Evidence only with `Failed: 0, Skipped: 0` and totals == N_S/N_A on the gated commit; no host/credential in docs | live gate + doc greps | 05-07 Task 3 verify (evidence commit ancestor of HEAD with docs-only diff; rebuild and recompute N_S/N_A from the skip path; each project's evidence line exactly `Total: N, Errors: 0, Failed: 0, Skipped: 0, Not Run: 0` or VSTest `Failed: 0, Passed: N, Skipped: 0, Total: N` with N == N_S/N_A; no nonzero counter; pending wording gone; no secret in added lines; flag grep). Failures are classified as test or production defects for gap closure | n/a | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

Test scaffolds are created inside each task, not in a separate wave. Plan 05-01 is the de-facto Wave 0: every live test depends on its harness.

- [ ] `CoffeePeek.Shops.Infrastructure.Tests/LivePostgres/LivePostgresServer.cs`: env gate, guards, create/drop/sweep, probes, `LiveDatabase<TContext>` (05-01)
- [ ] `<Compile Include=… Link="LivePostgres\LivePostgresServer.cs">` in `CoffeePeek.Account.Infrastructure.Tests.csproj` (05-01, D-01)
- [ ] `LivePostgresGuardTests.cs` (server-free) and `LivePostgresHarnessTests.cs` (probe self-tests) (05-01)
- [ ] `ShopsLiveDatabase.cs` (05-03), `ShopsBackfillRehearsalDatabase.cs` (05-04), `AccountLiveDatabase.cs` (05-05), `AccountBackfillRehearsalDatabase.cs` (05-06)
- [ ] No framework install needed.

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Providing and attesting a NON-production PostgreSQL 17 server and the `cp_live_tests` role | D-02, D-03, D-04 | No Docker on this machine. Only the user can supply and vouch for a non-production server and its credentials | 05-08 Task 1 (optional, early) and 05-07 Task 2 checkpoints; docs `## Live PostgreSQL gate` → "Server and role", "Credentials" |
| HTTP/YARP smoke: `/api/Users/by-slug/reviews` reaches AccountService; `ids=` arrays bind; hidden-profile 404 bodies identical except `traceId`; 503 while disabled | D-06, G-M1 | Needs a running gateway + services stack | docs `### HTTP smoke (manual)`; not claimed by the gate evidence |
| AccountService enablement decisions: D-02 slug growth / cooldown, deletion lock-hold measurement, rate-limit bucket | G-M2 | Product/ops decisions | docs Enablement gate step 5 |
| Enabling `PublicAddresses__Enabled` and write-mode backfill in any deployed environment | Deferred (CONTEXT) | Requires explicit user authorization; out of scope | docs runbook steps 3–6 |

---

## Validation Sign-Off

- [x] All tasks have `<automated>` verify or Wave 0 dependencies (the only exceptions are the 05-08 and 05-07 checkpoints, which are manual by nature and are each followed by an automated check)
- [x] Sampling continuity: no 3 consecutive tasks without automated verify
- [x] Wave 0 covers all MISSING references (05-01 harness first; fixtures inside their plans)
- [x] No watch-mode flags
- [x] Feedback latency < 120 s on the skip path
- [x] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
