---
status: complete
---

# Quick Task 260901-e0n: Fix 400 VALIDATION_FAILED on POST /api/ModerationReviews

**Plan:** [260901-e0n-PLAN.md](./260901-e0n-PLAN.md)
**Tasks:** 2/2 complete
**Duration:** ~3 min

## Root cause

`SendReviewToModerationCommand` required a nested `RatingDto Rating` object. The live client at
https://api.coffeepeek.by sends flat top-level `ratingCoffee`/`ratingPlace`/`ratingService` fields
instead, so `Rating` stayed unbound and ASP.NET Core's implicit-required-on-non-nullable-reference-type
model binding rejected the request with `"Rating": ["The Rating field is required."]` before the
handler ever ran.

## Fix

- `Rating` is now `RatingDto?` (nullable).
- Added `RatingCoffee`, `RatingPlace`, `RatingService` (`int?`, `[JsonPropertyName]`-annotated) to bind
  the flat client shape.
- Added a computed `EffectiveRating` property: `Rating ?? new RatingDto { Coffee = RatingCoffee ?? 0, ... }`.
- `SendReviewToModerationValidationStrategy` and `SendReviewToModerationHandler` now read
  `command.EffectiveRating` instead of `command.Rating`.
- `RatingDto.cs`, `CreateCheckInCommand`, `UpdateCoffeeShopReviewCommand`, and
  `CheckInCreatedHandler.cs` (the internal nested-shape caller) are untouched — the nested shape still
  works unchanged since `RatingDto` is assignable to `RatingDto?`.

## Commits

- `e5db088f`: fix(quick-260901-e0n): accept flat rating fields in SendReviewToModerationCommand
- `ab2aff98`: test(quick-260901-e0n): regression tests for flat/nested rating shapes on SendReviewToModerationCommand
- `8c66cc7f`: chore: merge quick task 260901-e0n rating field fix (worktree-agent-a10a88e847bed4ebd)

## Files touched

- `CoffeePeek.Moderation.Application/Features/Review/SendReviewToModeration/SendReviewToModerationCommand.cs`
- `CoffeePeek.Moderation.Application/Features/Review/SendReviewToModeration/SendReviewToModerationValidationStrategy.cs`
- `CoffeePeek.Moderation.Application/Features/Review/SendReviewToModeration/SendReviewToModerationHandler.cs`
- `CoffeePeek.ModerationService.Tests/SendReviewToModerationCommandBindingTests.cs` (new — 3 tests)

## Verification

- `dotnet build CoffeePeek.slnx` — 0 errors.
- `dotnet test CoffeePeek.ModerationService.Tests` — 4/4 pass (1 pre-existing + 3 new).
- `git diff` confirmed `RatingDto.cs`, `CreateCheckInCommand.cs`, `UpdateCoffeeShopReviewCommand.cs`,
  and `CheckInCreatedHandler.cs` are unchanged.

No deviations from plan.
