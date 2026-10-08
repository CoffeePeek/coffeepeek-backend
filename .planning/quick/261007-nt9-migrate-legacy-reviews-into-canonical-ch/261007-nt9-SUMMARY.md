---
phase: quick-261007-nt9
plan: "01"
subsystem: database
tags: [postgresql, minio, check-ins, reports]
requires:
  - phase: canonical-check-ins-on-main
    provides: Additive canonical tables and retained legacy archives
provides: [Operator backfill command, Historical protected photo copies, Retained complaint targets]
affects: [migration, privacy, ratings]
tech-stack:
  added: []
  patterns: [Explicit identity mapping, Private staged publication, Hashed restart manifest]
key-files:
  created: [dev/LegacyCheckInsMigration/Program.cs, dev/LegacyCheckInsMigration/LegacyReviewBackfill.cs]
  modified: [CoffeePeek.MediaService/Services/MinIOStorageService.cs, docs/migrations/unified-check-ins.md]
key-decisions:
  - Preserve Review UUIDs and report audit; never infer source links.
  - Publish only after snapshots and verified private photo bindings.
  - Require stopped writers plus drained broker/inbox/outbox before replacing counts.
requirements-completed: [MIG-01, MIG-04, CHECK-04, CHECK-05, PRIV-02, PRIV-03, PRIV-04, PRIV-05, PRIV-06, RATE-01, RATE-02]
duration: 34min from first RED gate
completed: 2026-10-07
---

# Quick 261007-nt9: Legacy reviews become canonical visits

Prepared an executable dry-run/apply/reconcile backfill preserving review UUIDs, complaint audit, historical snapshots/votes/counts and verified archive-preserving photo bindings. All three source tasks are complete; no deployed records were migrated.

| Task | RED | Implementation |
| --- | --- | --- |
| Visits, snapshots, audit/retry command | ebec8580 | 42c2809f |
| Historical private copies retaining source bytes | 997ea40e | ffc7b93a |
| Hidden-target admin complaints/rating fallback | c8854386 | 3dd3c81b |

Mapping retains published unlinked reviews, honours explicit pending/rejected/deleted evidence, uses explicit visit timestamps or documented review timestamp fallback, and keeps current galleries separate from earlier snapshots. Changed/native UUID targets are never overwritten. Source hashes preserve authored strings and null exactly; only known typed target columns allow UUID/timestamp equivalence. Synthetic Other drinks retain bounded custom names without a catalog row. Report target outcomes, exact counters and strict retryable cache invalidation are saved in the manifest.

Fixture totals: offline mapper 5 Reviews + 2 linked moderation rows become 5 canonical visits; photo fixture preserves all 6 evidenced photos with real Media UUIDs/order. The guarded PostgreSQL fixture specifies 5 reviews + 2 merged moderation + 1 merged visit + 1 native => 6 targets, 5 snapshots, 1 vote, 1 unchanged report; account count 9 => 6 with AddedShopsCount 7 unchanged. That PostgreSQL execution is NOT RUN.

Validation (logs under worktree .tmp): initial full CI 1,624 passed / 32 live-gate skips / zero failed (`backfill-ci-tests.log`); separate ModerationService 62 passed; full Media 74 passed, including 17 historical byte-stream checks. Audit RED reproduced six failures / 15 passes (`backfill-audit-red.log`, commit c6d99bb1); fix 288996f8 passed all 21 mapper cases (`backfill-audit-mapper.log`), then the affected Infrastructure suite passed 249 / skipped 23 / failed zero (`backfill-audit-green.log`). Final backend build includes the tool and has zero errors / 394 existing warnings (`backfill-audit-build.log`). Initial vulnerability audit found no vulnerable packages. CLI --help, diff whitespace and one-top-level-type checks passed; full CI was not repeated after the bounded audit fixes.

Operator commands/inputs/stage order/restore-only rollback are in `docs/migrations/unified-check-ins.md`. Actual PostgreSQL/full historical schema rehearsal, MinIO anonymous/signed HTTP, Redis faults, full gateway smoke, live apply and deployment are NOT RUN (Docker unavailable; COFFEEPEEK_LIVE_PG unset). Unknown live row/photo conflicts remain release blockers, not success.

Deviations: [Rule 1] bounded audit corrected synthetic Other validation and authored-string/null fingerprints; [Rule 2] required explicit broker drain plus persisted-envelope guard to prevent delayed creation events doubling account counts; [Rule 1] historical source deny targets Anonymous so signed recovery/retries work. Existing approval/photo/history APIs were reused; no dependency, migration-table or compatibility API added. SDK found no worktree global planning state; explicit quick-task scope leaves primary STATE/ROADMAP/user edits untouched. Only quick PLAN/SUMMARY are copied back.

## Self-Check: PASSED

All created source/test files and task/gate/audit commits exist; no tracked files were deleted, unfinished stubs remain or published migrations changed. Worktree feature branch retained; no push/live mutation performed.
