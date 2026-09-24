---
phase: quick-260924-jpd
plan: 01
subsystem: media, shops (check-ins), docs
tags: [security, photos, check-in, media, wolverine, codegen]
requires: []
provides:
  - POST /api/Photos/review (presign, prefix reviews/)
  - POST /api/Photos/resolve (caller-scoped trusted photo metadata)
  - CheckInPhotosAttachedEvent (Shops -> Media)
  - IUploadedPhotoLookup / MediaUploadedPhotoLookup (Shops -> Media HTTP)
affects: [CreateCheckInHandler, CheckInValidationStrategy, moderation ReviewDto.Photos]
tech-stack:
  added: []
  patterns: [cross-service HTTP lookup with X-Gateway-Auth + X-User-Id, fail-closed validation, outbox event + idempotent consumer, static-typeload-codegen]
key-files:
  created:
    - CoffeePeek.Contract/Dtos/ResolvedUploadedPhotoDto.cs
    - CoffeePeek.Contract/Events/Photo/CheckInPhotosAttachedEvent.cs
    - CoffeePeek.MediaService/Commands/ResolveUploadedPhotosQuery.cs
    - CoffeePeek.MediaService/Handlers/ResolveUploadedPhotosHandler.cs
    - CoffeePeek.MediaService/Consumers/CheckInPhotosAttachedHandler.cs
    - CoffeePeek.MediaService/Internal/Generated/WolverineHandlers/CheckInPhotosAttachedEventHandler1753508574.cs
    - CoffeePeek.MediaService/Internal/Generated/WolverineHandlers/ResolveUploadedPhotosQueryHandler1073871653.cs
    - CoffeePeek.MediaService.Tests/PhotosControllerTests.cs
    - CoffeePeek.MediaService.Tests/ResolveUploadedPhotosHandlerTests.cs
    - CoffeePeek.MediaService.Tests/CheckInPhotosAttachedHandlerTests.cs
    - CoffeePeek.Shops.Application/Abstractions/IUploadedPhotoLookup.cs
    - CoffeePeek.Shops.Infrastructure/Media/MediaUploadedPhotoLookup.cs
    - CoffeePeek.Shops.Application.Tests/Features/CheckIn/CheckInPhotoValidationTests.cs
    - CoffeePeek.Shops.Infrastructure.Tests/Media/MediaUploadedPhotoLookupTests.cs
    - docs/api/photos-client.md
    - docs/api/photos-admin.md
  modified:
    - CoffeePeek.MediaService/Controllers/PhotosController.cs
    - CoffeePeek.MediaService/Repositories/IPhotoRepository.cs
    - CoffeePeek.MediaService/Repositories/PhotoRepository.cs
    - CoffeePeek.MediaService/Services/Interfaces/IStorageService.cs
    - CoffeePeek.MediaService/Services/MinIOStorageService.cs
    - CoffeePeek.MediaService/Internal/Generated/WolverineHandlers/GeneratedHandlerRegistry.cs
    - CoffeePeek.MediaService.Tests/CoffeePeek.MediaService.Tests.csproj
    - CoffeePeek.Shops.Domain/BussinessConstants.cs
    - CoffeePeek.Shops.Application/ValidationStrategy/CheckIn/CheckInValidationStrategy.cs
    - CoffeePeek.Shops.Application/Features/CheckIn/CreateCheckIn/CreateCheckInHandler.cs
    - CoffeePeek.Shops.Infrastructure/DependencyInjection.cs
    - CoffeePeek.ShopsService/Internal/Generated/WolverineHandlers/CreateCheckInCommandHandler201570757.cs
    - CoffeePeek.Shops.Application.Tests/Features/CheckIn/CreateCheckInHandlerTests.cs
    - CoffeePeek.Shops.Application.Tests/Features/CheckIn/CheckInContractTests.cs
decisions:
  - "Check-in photo trust = Shops -> Media HTTP resolve scoped by X-User-Id; only storageKey from the client is used, and FileName/ContentType/Size come from Media metadata and the real MinIO object"
  - "Resolve fails closed: if Media is unreachable or rejects the call, a check-in with photos gets 400 'Photos could not be verified, please try again'; check-ins without photos never call Media"
  - "Reuse is blocked by Media status: resolve returns only Pending photos; CheckInPhotosAttachedEvent (outbox) flips them to Confirmed + is_permanent=true"
  - "Media resolve hard cap is 20 keys; the Shops limit is MaxCheckInPhotos = 5"
metrics:
  duration: ~55m
  completed: 2026-09-24
  tasks: 3
---

# Quick 260924-jpd Plan 01: Harden review and check-in photo attachment Summary

Check-ins now accept only photos the same user actually uploaded through MediaService. Shops resolves each storage key against Media over HTTP, caps the count at 5, rejects duplicate keys and keys already used, and saves Media-trusted metadata. After the check-in commits, Media marks the photos permanent and Confirmed. The plan also adds a `/api/Photos/review` presign endpoint and client/admin API docs.

## Commits

| Task | Commit | Description |
|------|--------|-------------|
| 1 | `3d95b4ba` | Media: `/review` presign, owner-scoped `/resolve`, `CheckInPhotosAttachedHandler`, `GetObjectSize`, `GetByStorageKeysAsync`, codegen, tests |
| 2 | `2f0d5495` | Shops: photo validation rules, `IUploadedPhotoLookup` + `MediaUploadedPhotoLookup`, trusted metadata, attach event, codegen, tests |
| 3 | `87af79fa` | `docs/api/photos-client.md`, `docs/api/photos-admin.md` |
| cleanup | `f915492c` | Test-only: `#nullable enable` + `TestContext.Current.CancellationToken` in the new tests (no new warnings in touched files) |

## Verification

- `dotnet build CoffeePeek.slnx`: succeeded, 0 errors. The warning count is pre-existing; no warnings come from touched files.
- `dotnet test CoffeePeek.slnx`: **12/12 projects green, 1143 passed, 0 failed, 0 skipped**.
  - MediaService.Tests 15/15 (includes `WolverinePreGeneratedHandlersTests`, 3 controller, 8 resolve, 3 consumer)
  - Shops.Application.Tests 174/174
  - Shops.Infrastructure.Tests 38/38 (includes 7 new lookup tests)
  - ShopsService.Tests 8/8 (includes `WolverinePreGeneratedHandlersTests`)
  - Shops.Domain 144, Moderation.Domain 191, Moderation.Application 5, ModerationService 31, Account.Domain 231, Account.Application 206, Account.Infrastructure 50, Gateway 50
- `git diff 61e83cdd..HEAD -- CoffeePeek.Contract`: only 2 added files (`A`); no existing contract type was modified.
- No EF migrations were added.
- Gateway: `media-Photos-route` `/api/Photos/{**remainder}` already covers `/review` and `/resolve`, so no gateway change was needed.

## Codegen procedure used

For both services: `dotnet run --no-launch-profile -- codegen write` with `ASPNETCORE_ENVIRONMENT=Production` and the `WolverinePreGeneratedHandlersTests` in-memory config passed as `Section__Key` env vars.
- MediaService: new `CheckInPhotosAttachedEventHandler1753508574.cs` and `ResolveUploadedPhotosQueryHandler1073871653.cs`, and the registry was updated.
- ShopsService: the stale `CreateCheckInCommandHandler201570757.cs` blocked compilation because the handler signature changed. I hand-patched that one call so the host would build. Codegen then rewrote the file with proper constructor injection of `MediaUploadedPhotoLookup` dependencies, and the committed file is the codegen output.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] `CheckInContractTests` also calls `CreateCheckInHandler.Handle`**
- **Found during:** Task 2
- **Issue:** This file was not in the plan's list. The new `IUploadedPhotoLookup` parameter broke its build. Its `PublishAsync(object)` callback also hard-cast every published message to `CheckinCreatedEvent`, which would throw on the new attach event.
- **Fix:** Added a lookup mock that resolves its key, and made the callback use `e as CheckinCreatedEvent ?? published`.
- **Files:** `CoffeePeek.Shops.Application.Tests/Features/CheckIn/CheckInContractTests.cs`
- **Commit:** `2f0d5495`

**2. [Rule 3 - Blocking] Stale pre-generated Shops handler prevented `codegen write` from building**
- **Fix:** One temporary manual edit so the host compiled, then real codegen overwrote it (see above). This matches the 260917-f9m procedure.
- **Commit:** `2f0d5495`

### Minor choices within the plan
- In the strategy, the empty/overlong-key check runs before the duplicate check. This way null keys cannot reach the ordinal `Distinct`. All three messages are exactly as specified.
- `UploadedPhotoLookupResult.Failed` is a static helper. `MediaUploadedPhotoLookup` also returns `Failed` without calling Media when `userId` is empty or no keys are passed.
- The public error strings are exposed as `CreateCheckInHandler.PhotosNotVerifiedMessage` / `PhotosNotFoundMessage` constants so the tests and docs can reference them.
- The `media-photo-lookup` HttpClient uses a 5 s timeout, the same as `account-user-lookup`. No extra resilience handler was added, because none is added for account-user-lookup either.

## Deferred Issues (out of scope, not fixed)

1. **`POST /api/ModerationReviews` still accepts unverified photos.** `SendReviewToModerationCommand.Photos` can be sent directly by any authenticated user. That path is not checked against Media, so the same forgery that was just closed for check-ins remains open there. Only check-in-originated reviews are hardened. Fixing it needs a Moderation -> Media lookup, which would reuse this pattern. This is noted in `photos-admin.md`.
2. **ShopPhoto column lengths versus Media metadata.** `ShopPhoto.FileName` is `varchar(50)` and `ContentType` is `varchar(30)` in the Shops DB. Media allows 255/100. A presigned photo with a long original file name (over 50 chars) makes the check-in fail at `SaveChanges` with a DB error (500) instead of a 400. This is pre-existing: client values had the same issue. Options are to truncate `FileName` in `CreateCheckInHandler` or widen the columns with a migration.
3. **Admin/owner gallery and menu attach endpoints** (`POST .../photos`, `.../menu/photos`) still trust the `UploadedPhotoDto` body and never mark photos permanent. These are privileged endpoints, but the gap is documented in `photos-admin.md`.
4. **Approved reviews are published without photos.** `ModerationReviewApprovedHandler` does not copy `ReviewDto.Photos` into the Shops `Review.Photos`. This is documented as "not currently supported".

## Threat Model Coverage

| Threat | Status |
|--------|--------|
| T-jpd-01 spoofing (foreign/unknown keys) | Mitigated: resolve is scoped to the caller's OwnerId and Shops rejects any missing key (tests: `Handle_WithUnknownOrForeignPhoto_ThrowsAndPersistsNothing`, `Handle_ForeignOwnerPhoto_IsOmitted`) |
| T-jpd-02 metadata tampering | Mitigated: saved ShopPhoto and ReviewDto.Photos use Media values and the real object size (`Handle_SpoofedClientMetadata_IsReplacedByResolvedMetadata`) |
| T-jpd-03 reuse | Mitigated: only Pending photos resolve, and the consumer sets Confirmed (`Handle_ConfirmedPhoto_IsOmitted`, `Handle_MarksOwnPendingPhotos...`) |
| T-jpd-04 resolve disclosure | Mitigated: owner comes from the principal only (`ResolveUploadedPhotos_OverridesOwnerFromPrincipal`) |
| T-jpd-05 DoS | Mitigated: 5 in Shops, 20 in Media, plus the gateway media-upload limit |
| T-jpd-06 Media down | Accepted: fails closed (`Handle_WhenMediaUnreachable_FailsClosed`) |
| T-jpd-07 lost attach event | Mitigated: published before SaveChanges through the durable outbox; the consumer is idempotent (`Handle_SecondDelivery_IsNoOp`) |

## Known Stubs

None.

## Self-Check: PASSED

- Files: all created files listed above exist on disk.
- Commits: `3d95b4ba`, `2f0d5495`, `87af79fa`, `f915492c` are present in `git log`.
