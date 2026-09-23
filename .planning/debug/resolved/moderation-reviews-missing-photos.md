---
slug: moderation-reviews-missing-photos
status: resolved
trigger: "GET /api/ModerationReviews does not return photos"
created: 2026-09-21
updated: 2026-09-21T12:31:00+03:00
---

# Debug Session: Moderation Reviews Missing Photos

## Symptoms

`GET /api/ModerationReviews` does not return review photos.

## User Evidence

- `QueryModerationReviewRepository.GetPagedAsync` eager-loads `ReviewPhotos`; `GetById` also includes them.
- Domain `ModerationReview` has `IReadOnlyCollection<PhotoMetadata> ReviewPhotos`.
- `CoffeePeek.Contract/Dtos/CoffeeShop/ModerationReviewDto.cs` omits photos.
- `MapsterConfiguration.ModerationReview.cs` only maps `CreatedAt` and does not map `ReviewPhotos` to `Photos`.
- The general moderation mapper maps domain `PhotoMetadata` to `PhotoMetadataDto` and constructs `FullUrl` using `MediaPublicUrlOptions`.

## Current Focus

reasoning_checkpoint:
  hypothesis: "The API omits review photos because both moderation-review handlers map loaded entities to ModerationReviewDto, which has no photo property, and the parent Mapster configuration has no ReviewPhotos-to-Photos mapping."
  confirming_evidence:
    - "QueryModerationReviewRepository.GetPagedAsync and GetById both Include(r => r.ReviewPhotos)."
    - "GetAllModerationReviewsHandler and GetModerationReviewByIdHandler both map results to ModerationReviewDto."
    - "ModerationReviewDto contains no photo collection, so JSON serialization cannot expose the loaded photos."
    - "MapsterConfiguration already defines PhotoMetadata-to-PhotoMetadataDto including FullUrl construction, but ConfigureModerationReview only maps CreatedAt."
  falsification_test: "The hypothesis would be false if either endpoint returned a response type containing photos through a separate projection or serializer extension; controller and handler tracing shows neither exists."
  fix_rationale: "Add a default-empty Photos collection to ModerationReviewDto and explicitly map ReviewPhotos to Photos, reusing the existing PhotoMetadata mapping so URLs are built consistently."
  blind_spots: "No runtime regression test was executed in diagnose-only mode; implementation should prove mapping and web JSON serialization with a review containing a photo."

**Smallest safe fix direction:** Add `PhotoMetadataDto[] Photos { get; init; } = [];` to `ModerationReviewDto`, register the existing `PhotoMetadata` child mapping before the parent moderation-review mapping, then add `.Map(dest => dest.Photos, src => src.ReviewPhotos)`. Add focused mapping and web-default serialization coverage in `CoffeePeek.ModerationService.Tests`, asserting photo metadata, a usable `FullUrl`, and default-empty JSON behavior.

**Next action:** Archive this resolved session and atomically commit only the four implementation/test files plus this debug document.

## Evidence

- timestamp: 2026-09-21T12:08:00+03:00
  checked: "CoffeeShop.Moderation.Persistence/Repositories/QueryModerationReviewRepository.cs"
  found: "Both GetPagedAsync and GetById eager-load ReviewPhotos."
  implication: "Photos are present at the persistence boundary; missing eager loading is not the cause."

- timestamp: 2026-09-21T12:08:00+03:00
  checked: "GetAllModerationReviewsHandler, GetModerationReviewByIdHandler, and ModerationReviewsController"
  found: "The list and detail endpoints both return mappings to the same ModerationReviewDto contract."
  implication: "A defect in this shared DTO/mapping boundary affects both endpoint shapes."

- timestamp: 2026-09-21T12:08:00+03:00
  checked: "CoffeePeek.Contract/Dtos/CoffeeShop/ModerationReviewDto.cs"
  found: "The response contract has no Photos or ReviewPhotos property."
  implication: "Loaded photo data cannot be represented or serialized in the API response."

- timestamp: 2026-09-21T12:08:00+03:00
  checked: "MapsterConfiguration.cs and MapsterConfiguration.ModerationReview.cs"
  found: "PhotoMetadata-to-PhotoMetadataDto already builds FullUrl from MediaPublicUrlOptions, while ModerationReview-to-ModerationReviewDto maps only CreatedAt and has no ReviewPhotos-to-Photos rule."
  implication: "The existing child mapping can be reused; only the response property and explicit differently-named collection mapping are missing."

- timestamp: 2026-09-21T12:08:00+03:00
  checked: "CoffeePeek.ModerationService.Tests project and ModerationRoasterDtoContractTests.cs"
  found: "The service test project references the service transitively and already tests Mapster mapping plus web JSON serialization."
  implication: "A focused contract regression test can cover both mapping and serialized API shape without introducing a new test project."

- timestamp: 2026-09-21T12:23:00+03:00
  checked: "Focused ModerationReviewDtoContractTests against the unfixed implementation"
  found: "Compilation fails with CS1061 at every assertion because ModerationReviewDto has no Photos member."
  implication: "The regression test directly reproduces the missing response contract at compile time before the fix."

- timestamp: 2026-09-21T12:27:00+03:00
  checked: "Focused ModerationReviewDtoContractTests after the contract and mapping fix"
  found: "Both tests pass: mapping exposes one photo with the expected public FullUrl, and web-default JSON includes both the populated collection and a default-empty photos array."
  implication: "The minimal fix restores the API response shape while preserving a non-null collection contract."

- timestamp: 2026-09-21T12:31:00+03:00
  checked: "Full CoffeePeek.ModerationService.Tests project and fix-only git diff"
  found: "All 8 tests pass. The intended diff contains only the DTO, two mapper files, the new focused contract test, and this debug session; unrelated working-tree changes remain untouched."
  implication: "The fix passes the broader relevant suite and is isolated for an atomic commit."

## Resolution

**Root cause:** `ReviewPhotos` is loaded from EF Core but discarded at the API projection boundary: `ModerationReviewDto` has no photo collection, and the `ModerationReview` Mapster configuration does not map the differently named `ReviewPhotos` source collection to a response `Photos` property.

**Fix:** Added a default-empty `Photos` array to `ModerationReviewDto`, registered photo child mappings before parent mappings, explicitly mapped `ReviewPhotos` to `Photos`, and added mapping plus web-default JSON regression coverage.

**Verification:** Focused `ModerationReviewDtoContractTests` passed 2/2. Full `CoffeePeek.ModerationService.Tests` passed 8/8. Existing package vulnerability and analyzer warnings remain unrelated to this fix.

**Files changed:** `CoffeePeek.Contract/Dtos/CoffeeShop/ModerationReviewDto.cs`, `CoffeePeek.Moderation.Application/Mapper/MapsterConfiguration.cs`, `CoffeePeek.Moderation.Application/Mapper/MapsterConfiguration.ModerationReview.cs`, `CoffeePeek.ModerationService.Tests/ModerationReviewDtoContractTests.cs`.
