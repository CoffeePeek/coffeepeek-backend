# Phase 5: Operational backfill, compatibility and rollout verification - Context

**Gathered:** 2026-09-29
**Status:** Ready for planning
**Source:** User direction ("fix the flagged warnings, then run the backfill and rollout checks; ask when database credentials are needed") + research recommendations Q1–Q10 adopted as defaults

<domain>
## Phase Boundary

Opt-in live PostgreSQL suite that closes the phase 1–4 enablement gate against a user-provided non-production PostgreSQL 17 server, a real-SQL backfill/rollback rehearsal per service, the Shops backfill 409 fix, and the operator runbook. No production data touched; no flag enabled in any deployed environment without explicit user authorization.

</domain>

<decisions>
## Implementation Decisions

- D-01: Suite lives in the existing `CoffeePeek.Shops.Infrastructure.Tests` and `CoffeePeek.Account.Infrastructure.Tests`, tagged `[Trait("Category","LivePostgres")]`; one shared helper linked into the Account project. No new test project, no new packages.
- D-02: Opt-in via env var `COFFEEPEEK_LIVE_PG` (admin connection string to database `postgres`, password via pgpass). Unset → tests report Skipped and CI/`dotnet test CoffeePeek.slnx` stay green.
- D-03: Safety guards: refuse a superuser role unless `COFFEEPEEK_LIVE_PG_ALLOW_SUPERUSER=1`; only create/drop databases named `cp_live_*`; always drop on success and failure.
- D-04: A gate run counts only with `Failed: 0, Skipped: 0` for the LivePostgres category. The executor runs it if `COFFEEPEEK_LIVE_PG` is visible; otherwise the user runs it and pastes only summary lines. Never claim the gate closed from a skipped run.
- D-05: Fix Shops `PublicAddressService.BackfillAsync` to map an in-flight unique violation to 409 (Account-style), with a test.
- D-06: HTTP/YARP smoke stays manual runbook commands.
- D-07: Runbook backfill loop is a docs snippet (page-by-page: dry-run page N → review → write page N → next), not a new script.
- D-08: After a green run, record date, PostgreSQL version, commit and test counts in `docs/api/public-addresses.md`; `PublicAddresses__Enabled` stays unset everywhere.
- D-09: CI job with a Postgres service container is deferred.
- D-10: Rehearsal against a production schema dump is deferred.

### Claude's Discretion
- Exact test/fixture file names, lock-probe helpers, throwaway DB naming suffix.

</decisions>

<canonical_refs>
## Canonical References

- `.planning/phases/05-operational-backfill-compatibility-and-rollout-verification/05-RESEARCH.md` — test design per gate item, pitfalls, credential requirements
- `docs/api/public-addresses.md` — enablement gate checklist to turn into tests
- `.planning/specs/PUBLIC-SLUGS.md` — steps 4 and 7

</canonical_refs>

<deferred>
## Deferred Ideas

- CI Postgres service container job (D-09).
- Rehearsal against a production schema dump (D-10).
- Enabling `PublicAddresses__Enabled` / running write-mode backfill in any real environment (requires explicit user authorization).

</deferred>
