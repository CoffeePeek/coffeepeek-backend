---
status: complete
quick_id: 260924-oxa
date: 2026-09-24
---

# Quick Task 260924-oxa: load moderation review photos on approve + fullUrl on review photos

This follows up 260924-mii. The Shops consumer copied photos correctly, but the event it received was always empty.

## Root cause
The approve handler calls `ModerationReviewRepository.GetById` (`CoffeeShop.Moderation.Persistence`), and it never did `.Include(ReviewPhotos)`. As a result, `ModerationReviewApprovedEvent.Review.Photos` and `ModerationReviewEditedEvent.Review.Photos` were always `[]`. The admin list uses `QueryModerationReviewRepository`, which does include photos, so it looked fine there. 260924-mii's handler tests built the event by hand, so they never went through this repository.

## Changes
- `4f60e488`: `GetById` includes `ReviewPhotos`. Adds an InMemory round-trip test, `ModerationReviewRepositoryTests`. Verified: it fails without the Include.
- `340b8a12`: adds an optional field `UploadedPhotoDto.FullUrl` (additive, `null` by default). The Shops mapper fills it from the shops bucket. Review photos are ordered by `SortIndex`. Adds a mapper test. `docs/api/photos-client.md` is corrected.

## Verification
`dotnet build CoffeePeek.slnx`: 0 errors. `dotnet test CoffeePeek.slnx`: all 1152 tests pass.

## Follow-up
Re-run the 260924-mii backfill scripts. They fill reviews approved before this deploy (e.g. 2a7ad794) that still have zero photos.
