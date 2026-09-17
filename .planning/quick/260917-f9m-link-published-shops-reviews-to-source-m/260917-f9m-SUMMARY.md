---
phase: 260917-f9m
plan: 01
subsystem: [shops, moderation, contract]
tags: [reviews, moderation, wolverine, ef-migration, cross-service-events]
requires:
  - Contract.ModerationReviewApprovedEvent
  - IReviewRepository
  - IQueryModerationReviewRepository
provides:
  - ReviewDto.ModerationReviewId
  - ModerationReviewEditedEvent
  - ModerationReviewLinkedEvent
  - Review.ModerationReviewId (shops link column)
  - ModerationReview.Resubmit
  - "POST /api/ModerationReviews/backfill-links"
affects:
  - Shops review publication path (approve/edit)
  - Moderation review edit path
tech-stack:
  added: []
  patterns: [wolverine-tuple-return-event, ef-core-migration, static-typeload-codegen]
key-files:
  created:
    - CoffeePeek.Contract/Events/Moderation/ModerationReviewEditedEvent.cs
    - CoffeePeek.Contract/Events/Moderation/ModerationReviewLinkedEvent.cs
    - CoffeePeek.Shops.Infrastructure/Consumers/ModerationReviewEditedHandler.cs
    - CoffeePeek.Shops.Infrastructure/Consumers/ModerationReviewLinkedHandler.cs
    - CoffeePeek.Moderation.Application/Features/Review/BackfillReviewLinks/BackfillReviewLinksCommand.cs
    - CoffeePeek.Moderation.Application/Features/Review/BackfillReviewLinks/BackfillReviewLinksHandler.cs
    - CoffeePeek.Shops.Persistance/Migrations/20260917081130_AddModerationReviewIdToReviews.cs
    - CoffeePeek.Shops.Application.Tests/Features/Review/ModerationReviewApprovedHandlerTests.cs
    - CoffeePeek.Shops.Application.Tests/Features/Review/ModerationReviewEditedHandlerTests.cs
  modified:
    - CoffeePeek.Contract/Dtos/CoffeeShop/ReviewDto.cs
    - CoffeePeek.Moderation.Domain/Aggregates/ModerationReviewAggregate/ModerationReview.Action.cs
    - CoffeePeek.Moderation.Application/Features/Review/UpdateCoffeeShopReview/UpdateCoffeeShopReviewHandler.cs
    - CoffeePeek.Shops.Domain/Aggregates/ReviewAggregate/Review.cs
    - CoffeePeek.Shops.Domain/Aggregates/ReviewAggregate/ReviewAction.cs
    - CoffeePeek.Shops.Domain/Aggregates/ReviewAggregate/IReviewRepository.cs
    - CoffeePeek.Shops.Persistance/Repositories/ReviewRepository.cs
    - CoffeePeek.Shops.Persistance/Configuration/ShopsDbContext.cs
    - CoffeePeek.Shops.Infrastructure/Consumers/ModerationReviewApprovedHandler.cs
    - CoffeePeek.ModerationService/Controllers/ModerationReviewsController.cs
    - CoffeePeek.Shops.Application/Features/CheckIn/CreateCheckIn/CreateCheckInHandler.cs
decisions:
  - "Reuse Review.IsSoftDelete as the hide mechanism (zero new query filter points) rather than a new IsHidden column"
  - "Author edit uses ModerationReview.Resubmit (no ModeratedBy/ModeratedAt stamp) instead of MoveToPending, for correct audit semantics"
  - "CreateCheckIn transient review is unlinked: pass Guid.Empty to Create and null the DTO's ModerationReviewId"
metrics:
  duration: ~11 min
  completed: 2026-09-17
---

# Phase 260917-f9m Plan 01: Link Published Shops Reviews to Source Moderation Review Summary

Links every published shops `Review` to its source `ModerationReview` via a nullable `ModerationReviewId`, routes user edits through re-moderation (edit -> Pending + hide published review), makes re-approval update the SAME row instead of silently skipping, and adds an idempotent moderator backfill endpoint — eliminating the PUT `/api/ModerationReviews/{id}` 404.

## What Was Built

- **Contract:** `ReviewDto.ModerationReviewId` (nullable), plus `ModerationReviewEditedEvent` and `ModerationReviewLinkedEvent`.
- **Moderation edit:** `ModerationReview.Resubmit(authorId)` resets an edited review to Pending without a moderator stamp; `UpdateCoffeeShopReviewHandler` now returns a Wolverine tuple that auto-publishes `ModerationReviewEditedEvent`.
- **Shops domain/persistence:** `Review.ModerationReviewId` link column (+ index, nullable EF migration, not applied); `Create` takes the id; new `UpdateContent/Hide/Restore/LinkToModerationReview`; repo lookups `GetByModerationReviewId` (includes hidden rows) and `GetUnlinkedByShopAndUser`.
- **Shops consumers:** approve now upserts by moderation-review id (update+restore existing, else create linked) — removing the `AnyAsync` skip that dropped every re-approval; new edited handler hides the published review; new linked handler backfills the id idempotently.
- **Backfill:** moderator-only `POST /api/ModerationReviews/backfill-links` publishes one `ModerationReviewLinkedEvent` per approved review.
- **Tests:** 4 new consumer tests (approve-updates-existing regression guard, approve-creates-linked, edit-hides, edit-no-op-when-unlinked).

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Stale Wolverine generated handlers (`TypeLoadMode.Static`)**
- **Found during:** Tasks 2, 4, 5.
- **Issue:** The services run Wolverine with `TypeLoadMode.Static` (non-Development) and check in pre-generated handler code under `Internal/Generated/WolverineHandlers/`, enforced by `WolverinePreGeneratedHandlersTests`. Changing the `UpdateCoffeeShopReview` handler signature and adding three new handlers left this directory out of sync (build error + would crash-loop in prod).
- **Fix:** Ran `dotnet run -- codegen write` for both `CoffeePeek.ShopsService` and `CoffeePeek.ModerationService` (with the same in-memory config the regression tests use) and committed the regenerated handlers + `GeneratedHandlerRegistry.cs`. For Task 2 the moderation handler was hand-edited first to unblock the build; codegen later produced an identical (whitespace-normalized) file.
- **Incidental:** codegen also emitted a previously-missing `SetReviewHelpfulCommandHandler` — a pre-existing codegen gap that is now closed.
- **Files:** `CoffeePeek.ShopsService/Internal/Generated/**`, `CoffeePeek.ModerationService/Internal/Generated/**`.

**2. [Rule 3 - Blocking] Second `Review.Create` caller not in plan (`CreateCheckInHandler`)**
- **Found during:** Task 3.
- **Issue:** The plan named only `ModerationReviewApprovedHandler` as a `Review.Create` caller, but `CreateCheckInHandler` also builds a transient `Review` (never persisted) purely to shape a `ReviewDto` for `CheckinCreatedEvent`. The new required `moderationReviewId` parameter broke its build.
- **Fix:** Pass `Guid.Empty` (this review is genuinely unlinked) and explicitly set `ModerationReviewId = null` on the produced DTO so unlinked semantics are preserved.
- **Files:** `CoffeePeek.Shops.Application/Features/CheckIn/CreateCheckIn/CreateCheckInHandler.cs`.

**3. [Rule 3 - Blocking] Existing `Review.Create` test callers**
- **Found during:** Task 6.
- **Issue:** `ReviewTests` (Shops.Domain.Tests), `SetReviewHelpfulHandlerTests`, `DeleteReviewFromCoffeeShopHandlerTests` used the old `Create` arity, breaking the full-solution build.
- **Fix:** Appended a `moderationReviewId` argument to each; also strengthened `ReviewTests` valid-data case to assert the link is set.

## Threat Model Compliance

- **T-f9m-01** (edited-but-unapproved visible): mitigated — `ModerationReviewEditedHandler.Hide()` sets `IsSoftDelete`; all public queries already exclude soft-deleted rows.
- **T-f9m-02** (edit another user's review): mitigated — existing `review.UserId != command.UserId` -> Forbidden retained.
- **T-f9m-03** (backfill double-link): mitigated — `LinkToModerationReview` only sets when null and the query filters `ModerationReviewId == null`; re-runnable.
- **T-f9m-04** (non-moderator backfill): mitigated — endpoint gated `[Authorize(Policy = RoleConsts.Moderator)]`.
- **T-f9m-SC:** No new NuGet packages added.

## Verification

- `dotnet build CoffeePeek.slnx` — Build succeeded, 0 errors.
- `dotnet test CoffeePeek.Shops.Application.Tests` — Passed! 145/145 (4 new tests included).
- Migration `20260917081130_AddModerationReviewIdToReviews` adds a nullable `ModerationReviewId` column + index to `Reviews`; NOT applied to any database.

## Known Stubs

None.

## Self-Check: PASSED

- All created files verified present on disk.
- All 6 task commits present in git log (7a0dac73, 2dd99274, f8ef285a, 0bab7cc5, e84ca50b, cda8f15a).
