---
phase: quick-account-deletion-workflow
plan: 01
subsystem: account
tags: [account-deletion, ef-core, postgres, wolverine, resend, hmac]
requires: []
provides:
  - Pending account deletion request lifecycle
  - Authenticated request/status APIs and public token cancellation
  - HMAC-protected confirmation tokens and confirmation email
affects: [account-deletion-confirmation, cross-service-deletion, frontend]
tech-stack:
  added: []
  patterns: [opaque-token-hmac-storage, partial-unique-active-request, immediate-email-event]
key-files:
  created:
    - CoffeePeek.Account.Domain/Entities/AccountDeletionRequestAggregate/AccountDeletionRequest.cs
    - CoffeePeek.Account.Persistence/Migrations/20260921100539_AddAccountDeletionRequests.cs
    - CoffeePeek.Account.Infrastructure/Identity/AccountDeletionTokenService.cs
    - CoffeePeek.Account.Infrastructure/Consumers/AccountDeletionRequestedEventHandler.cs
  modified:
    - CoffeePeek.Account.Application/Features/User/DeleteUser/DeleteUserHandler.cs
    - CoffeePeek.AccountService/Controllers/UsersController.cs
key-decisions:
  - "Deletion requests leave the user and sessions active until a later confirmation phase."
  - "Confirmation tokens are 256-bit opaque values stored only as peppered HMAC-SHA256 hashes."
  - "A PostgreSQL partial unique index prevents concurrent active requests per user."
requirements-completed: []
duration: 14min
completed: 2026-09-21
---

# Account Deletion Workflow Slice Summary

**Pending account-deletion requests with HMAC-protected confirmation, cancellation, persistence, and immediate Resend email delivery**

## Performance
- **Duration:** 14 min
- **Started:** 2026-09-21T10:01:00Z
- **Completed:** 2026-09-21T10:15:00Z
- **Tasks:** 3
- **Files modified:** 52

## Accomplishments
- Replaced immediate user soft deletion/session revocation with a pending request and `202 Accepted` response.
- Added current-request lookup and public token-protected cancellation without implementing confirmation or cleanup.
- Added validated pepper configuration, cryptographically random opaque tokens, HMAC-only storage, EF migration, and deletion confirmation email delivery.

## Task Commits
1. **Domain model, repository, configuration, and migration** - `dea2ae1f`
2. **Request, lookup, cancellation APIs, and token security** - `c0a657b6`
3. **Deletion confirmation template and Resend consumer** - `94006a02`

## Files Created/Modified
- `CoffeePeek.Account.Domain/Entities/AccountDeletionRequestAggregate/` - Request lifecycle, statuses, reasons, partition state, repository contract, and immediate event.
- `CoffeePeek.Account.Persistence/Configuration/AccountDeletionRequestConfiguration.cs` - Mapping, row version, token hash index, and active-request partial unique index.
- `CoffeePeek.Account.Persistence/Migrations/20260921100539_AddAccountDeletionRequests.cs` - Account database schema migration.
- `CoffeePeek.Account.Application/Features/User/DeleteUser/` - Create/resend, current request, and cancellation handlers.
- `CoffeePeek.Account.Infrastructure/Identity/AccountDeletionTokenService.cs` - Random token generation and HMAC-SHA256 hashing.
- `CoffeePeek.Account.Infrastructure/Consumers/AccountDeletionRequestedEventHandler.cs` - Resend confirmation email without sensitive logging.
- `CoffeePeek.AccountService/Controllers/UsersController.cs` - `202` request endpoint, authenticated GET, and public cancellation endpoint.

## Decisions Made
- Existing pending requests are idempotently returned during cooldown; after cooldown their token and expiry are rotated.
- Active means `PendingConfirmation` or `Processing`; the database enforces one such row per user.
- Cancellation accepts only an unexpired pending token and does not alter the user or sessions.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Regenerated Wolverine static handlers**
- **Found during:** API and email tasks.
- **Issue:** Checked-in generated handlers no longer matched changed/new handler signatures.
- **Fix:** Regenerated AccountService Wolverine handlers with `dotnet run -- codegen write`.
- **Verification:** Account service build and static-handler regression test pass.
- **Committed in:** `c0a657b6`, `94006a02`

**2. [Rule 3 - Blocking] Added missing xUnit import**
- **Found during:** Final focused test run.
- **Issue:** The new domain test file did not inherit a global xUnit import.
- **Fix:** Added the explicit `Xunit` using.
- **Verification:** 228 domain tests pass.
- **Committed in:** `94006a02`

## Issues Encountered
- Context7 documentation access was unavailable due an invalid configured API key, and the local `ctx7` fallback was not installed. No package or API substitution was required.
- Existing NuGet audit warnings remain for `Microsoft.OpenApi` and `OpenTelemetry.Api`; they predate and are outside this slice.

## User Setup Required
- Set `AccountDeletionOptions__TokenPepper` to a secret value of at least 32 characters in deployed environments. Do not reuse the development placeholder.

## Next Phase Readiness
- Ready for token confirmation and orchestrated cross-service deletion in a later slice.
- No confirmation, Redis/Gateway changes, cleanup, Media work, completion email, or frontend was added.

## Self-Check: PASSED
- All three task commits exist.
- Migration, model snapshot, handlers, tests, and generated Wolverine consumers exist.
- Focused domain, application, and infrastructure suites pass; AccountService builds successfully.
