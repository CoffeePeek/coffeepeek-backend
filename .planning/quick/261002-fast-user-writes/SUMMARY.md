---
status: complete
quick_id: 261002-fast-user-writes
slug: fast-user-writes
completed: 2026-10-03
---

# SUMMARY: Faster user submissions

## Changes

- Kept the synchronous 200/201 contract: a successful response contains an ID after the database transaction commits.
- Moved moderation shop and roaster geocoding to durable local Wolverine queues. The submitted address is saved as unvalidated; the consumer updates only a still-pending row with the same address. Transient Yandex failures retry, and no result leaves the address for moderator review.
- Moved check-in Redis cache invalidation to a durable local queue in the Shops outbox transaction. The consumer uses strict Redis calls so failures retry. Search cache entries expire by the next UTC minute and public platform stats have a five-minute TTL if invalidation is delayed.
- Replaced roaster catalog download during duplicate checking with an authenticated exact-name existence endpoint. Independent slug and catalog lookups in shop and change-request submission now run concurrently. Interactive Shops and Media HTTP lookups have five-second timeouts.
- Added low-cardinality OpenTelemetry spans and duration metrics for submission stages. No user names, addresses, or IDs are metric labels.
- Regenerated static Wolverine handlers for the changed messages and consumers.

## Verification

- `dotnet build CoffeePeek.Backend.ci.slnf -c Debug --no-restore -m:1 /nodeReuse:false -v:q`: 0 errors; existing warnings remain.
- `dotnet test CoffeePeek.Backend.ci.slnf -c Debug --no-build -m:1 /nodeReuse:false -v:q`: 1,564 passed, 52 skipped live-infrastructure tests, 0 failed.
- `dotnet test CoffeePeek.ModerationService.Tests/CoffeePeek.ModerationService.Tests.csproj -c Debug --no-build -m:1 /nodeReuse:false -v:q`: 50 passed, 0 failed.
- Generated adapters were inspected: create handlers enlist the EF outbox, save, and commit before the response; consumers have separate local-queue handlers.
- On 2026-10-03, PostgreSQL 17 integration checks passed on temporary databases. Moderation repositories persisted geocoding coordinates and rejected repeat or stale-address delivery for shops and roasters. The Shops roaster existence query matched case-insensitively and treated `%`/`_` literally. Temporary resources were removed; application databases were not accessed.
- End-to-end request p50/p95 and Redis queue behavior remain unmeasured live. Capture request and stage histograms after deploying this commit before claiming a measured latency improvement.

## Operational notes

- A new shop or roaster with an address is temporarily `IsAddressValidated=false`; approval remains blocked until the worker validates it or a moderator resolves it.
- Check-in cache reads can briefly be stale after a committed write. Redis failures are logged and retried through Wolverine; exhausted messages require error-queue inspection.
- Review, change-request, and report writes still wait for their database commit. Their added timings show where to optimize next if their latency remains high.
