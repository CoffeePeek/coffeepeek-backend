---
phase: quick-260924-jpd
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - CoffeePeek.Contract/Dtos/ResolvedUploadedPhotoDto.cs
  - CoffeePeek.Contract/Events/Photo/CheckInPhotosAttachedEvent.cs
  - CoffeePeek.MediaService/Controllers/PhotosController.cs
  - CoffeePeek.MediaService/Commands/ResolveUploadedPhotosQuery.cs
  - CoffeePeek.MediaService/Handlers/ResolveUploadedPhotosHandler.cs
  - CoffeePeek.MediaService/Consumers/CheckInPhotosAttachedHandler.cs
  - CoffeePeek.MediaService/Repositories/IPhotoRepository.cs
  - CoffeePeek.MediaService/Repositories/PhotoRepository.cs
  - CoffeePeek.MediaService/Services/Interfaces/IStorageService.cs
  - CoffeePeek.MediaService/Services/MinIOStorageService.cs
  - CoffeePeek.MediaService/Internal/Generated/WolverineHandlers/*
  - CoffeePeek.MediaService.Tests/CoffeePeek.MediaService.Tests.csproj
  - CoffeePeek.MediaService.Tests/PhotosControllerTests.cs
  - CoffeePeek.MediaService.Tests/ResolveUploadedPhotosHandlerTests.cs
  - CoffeePeek.MediaService.Tests/CheckInPhotosAttachedHandlerTests.cs
  - CoffeePeek.Shops.Domain/BussinessConstants.cs
  - CoffeePeek.Shops.Application/Abstractions/IUploadedPhotoLookup.cs
  - CoffeePeek.Shops.Application/ValidationStrategy/CheckIn/CheckInValidationStrategy.cs
  - CoffeePeek.Shops.Application/Features/CheckIn/CreateCheckIn/CreateCheckInHandler.cs
  - CoffeePeek.Shops.Infrastructure/Media/MediaUploadedPhotoLookup.cs
  - CoffeePeek.Shops.Infrastructure/DependencyInjection.cs
  - CoffeePeek.ShopsService/Internal/Generated/WolverineHandlers/*
  - CoffeePeek.Shops.Application.Tests/Features/CheckIn/CreateCheckInHandlerTests.cs
  - CoffeePeek.Shops.Application.Tests/Features/CheckIn/CheckInPhotoValidationTests.cs
  - CoffeePeek.Shops.Infrastructure.Tests/Media/MediaUploadedPhotoLookupTests.cs
  - docs/api/photos-client.md
  - docs/api/photos-admin.md
autonomous: true
requirements: [QUICK-260924-jpd]

must_haves:
  truths:
    - "A check-in with more than MaxCheckInPhotos photos, duplicate storage keys, or empty keys is rejected with 400"
    - "A check-in referencing a storage key that is unknown, owned by another user, already confirmed, expired, or never actually uploaded to MinIO is rejected with 400"
    - "ShopPhoto rows and the moderation ReviewDto.Photos use FileName/ContentType from Media metadata and Size from the real MinIO object, never the client body"
    - "After a check-in is saved, its photos become is_permanent=True in MinIO and Status=Confirmed in Media, so the same key cannot be attached twice"
    - "POST /api/photos/review returns presigned URLs whose storage keys start with reviews/; POST /api/photos/shop still works unchanged"
    - "docs/api/photos-client.md and docs/api/photos-admin.md describe the full flow with exact headers, limits and error messages"
  artifacts:
    - path: "CoffeePeek.Contract/Events/Photo/CheckInPhotosAttachedEvent.cs"
      provides: "Additive contract event Shops -> Media"
      contains: "record CheckInPhotosAttachedEvent"
    - path: "CoffeePeek.MediaService/Handlers/ResolveUploadedPhotosHandler.cs"
      provides: "Owner-scoped lookup of pending uploaded photos"
    - path: "CoffeePeek.MediaService/Consumers/CheckInPhotosAttachedHandler.cs"
      provides: "MarkAsPermanent + Confirmed on attach"
    - path: "CoffeePeek.Shops.Infrastructure/Media/MediaUploadedPhotoLookup.cs"
      provides: "HTTP client to Media resolve endpoint (AccountUserExistenceLookup pattern)"
    - path: "docs/api/photos-client.md"
      provides: "Client contract"
    - path: "docs/api/photos-admin.md"
      provides: "Admin/moderator contract"
  key_links:
    - from: "CreateCheckInHandler"
      to: "IUploadedPhotoLookup.ResolveAsync"
      via: "called after validationStrategy, before CheckIn.AddPhotos"
      pattern: "ResolveAsync"
    - from: "MediaUploadedPhotoLookup"
      to: "POST /api/Photos/resolve on coffeepeek-mediaservice"
      via: "named HttpClient media-photo-lookup + X-Gateway-Auth + X-User-Id"
      pattern: "api/Photos/resolve"
    - from: "CreateCheckInHandler"
      to: "CheckInPhotosAttachedHandler (Media)"
      via: "bus.PublishAsync(CheckInPhotosAttachedEvent) through durable outbox + conventional RabbitMQ routing"
      pattern: "CheckInPhotosAttachedEvent"
---

<objective>
Close the check-in/review photo forgery gap: Shops must only attach photos the current user really uploaded via MediaService, with trusted metadata, a count cap, and no reuse; attached photos become permanent. Add a review-specific presign endpoint and write client + admin API docs.

Purpose: Today CheckInValidationStrategy ignores Photos entirely, so any client can attach arbitrary storage keys (other users' photos, shop gallery images, non-existent objects) with fake ContentType/Size, and those flow into moderation and published reviews. Uploaded photos are also never marked permanent (ConfirmPhoto/MarkAsPermanent are dead code), so MinIO lifecycle rules may expire attached photos.

Output: Media resolve endpoint + attach consumer + /review presign; Shops photo validation via existing cross-service HTTP lookup pattern; tests; two docs.
</objective>

<chosen_mechanism>
Validation = Shops -> Media HTTP lookup, reusing the existing `AccountUserExistenceLookup` pattern (named HttpClient to `http://{AppResources.XxxService}` + `request.AddGatewayAuthHeader(GatewayAuthOptions.SecretKey)`).
- Key-prefix ownership is NOT viable: `MinIOStorageService.GetPresignedUploadUrl` generates keys as `{prefix}/{Guid.NewGuid()}{ext}` with no user id, and `PhotoMetadata` (Media DB) is the only record of ownership. No service consumes `PhotosUploadedEvent` and Shops has no local upload table, so a lookup is the zero-migration option.
- Shops calls Media directly over service discovery (not through the gateway) with `X-Gateway-Auth` + `X-User-Id = command.UserId`. `HeaderAuthenticationHandler` turns that into the user principal, and the Media endpoint scopes by `userContext.GetUserIdOrThrow()`. The endpoint therefore only ever returns the caller's own photos. Even though the gateway route `/api/Photos/{**remainder}` also exposes it publicly, a client learns nothing it does not already own. No new "internal-only" infra is needed.
- Reuse is blocked by status: resolve only returns `Status == Pending`, not `IsExpired`, and objects that exist in MinIO. Once the check-in commits, `CheckInPhotosAttachedEvent` flips them to `Confirmed`. The window between commit and the consumer is covered by the per-user check-in lock plus the `MinHoursBetweenUserCheckIns` cooldown.
- The lookup fails closed. If Media is unreachable or returns non-success, the check-in with photos is rejected; check-ins without photos never call Media.
</chosen_mechanism>

<execution_context>
@$HOME/.claude/get-shit-done/workflows/execute-plan.md
@$HOME/.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@.planning/STATE.md
@./CLAUDE.md
@CoffeePeek.MediaService/Controllers/PhotosController.cs
@CoffeePeek.MediaService/Handlers/GenerateShopPhotoHandler.cs
@CoffeePeek.MediaService/Handlers/ConfirmPhotoHandler.cs
@CoffeePeek.MediaService/Consumers/DeletePhotoHandler.cs
@CoffeePeek.MediaService/Services/MinIOStorageService.cs
@CoffeePeek.MediaService/Data/PhotoMetadata.cs
@CoffeePeek.Shops.Infrastructure/Account/AccountUserExistenceLookup.cs
@CoffeePeek.Shops.Infrastructure/DependencyInjection.cs
@CoffeePeek.Shops.Application/ValidationStrategy/CheckIn/CheckInValidationStrategy.cs
@CoffeePeek.Shops.Application/Features/CheckIn/CreateCheckIn/CreateCheckInHandler.cs
@CoffeePeek.Shops.Application.Tests/Features/CheckIn/CreateCheckInHandlerTests.cs
@docs/frontend-shop-photo-order.md

<interfaces>
Existing (verified):
- Contract `UploadedPhotoDto(FileName, ContentType, StorageKey, Size)` in CoffeePeek.Contract/Dtos/UploadedPhotoDto.cs (keep it unchanged; clients still send it).
- Contract events live in namespace `CoffeePeek.Contract.Events` (folder Events/Photo), e.g. `PhotoConfirmedEvent(Guid PhotoId, string StorageKey, string OwnerType, Guid OwnerId, Guid EntityId, DateTime ConfirmedAt)`.
- Media `IStorageService`: `GetPresignedUploadUrl(fileName, contentType, BucketType, ct, keyPrefix)`, `MarkAsPermanent(storageKey, BucketType, ct)`, `Exists(storageKey, BucketType, ct)` (uses StatObjectAsync), `Delete(...)`.
- Media `IPhotoRepository`: `Add`, `AddRange`, `GetByIdAsync`. `PhotoRepository(MediaDbContext dbContext)` uses `dbContext.Photos`.
- `PhotoMetadata`: `Id, FileName, ContentType, StorageKey, SizeBytes, UploadedAt, Status (PhotoStatus: Pending, Confirmed?, PendingDeletion, Deleted), BucketType, OwnerType (User|Shop), OwnerId, IsExpired`.
- Media `GenerateShopPhotosCommand(requests, ownerId, storageKeyPrefix?)`: `/menu` passes "menus", `/roaster` passes "roasters".
- Presign signs headers `Content-Type: {contentType}` and `x-amz-tagging: is_permanent=False`, expiry 600s (`PresignedUrlExpirySeconds`).
- Shared.Auth: `GatewayHeaderConsts.XGatewayAuth`, `GatewayHeaderConsts.XUserId`; `request.AddGatewayAuthHeader(secret)`; `IOptions<GatewayAuthOptions>`; `AppResources.MediaService = "coffeepeek-mediaservice"`.
- Shops `BusinessConstants` in CoffeePeek.Shops.Domain/BussinessConstants.cs (file name typo; do not rename). No existing check-in photo constant.
- `CreateCheckInHandler.Handle(command, IQueryCheckInRepository, IUnitOfWork, IMessageBus, IAsyncValidationStrategy<CreateCheckInCommand>, IMapper, ICacheService, ct)`.
- Wolverine prod uses `TypeLoadMode.Static`: any new or changed handler requires `dotnet run -- codegen write` in the service host (see CoffeePeek.MediaService.Tests/WolverinePreGeneratedHandlersTests.cs for the in-memory config to pass, and the 260917-f9m SUMMARY for the procedure).
</interfaces>
</context>

<tasks>

<task type="auto" tdd="true">
  <name>Task 1: Media side: /review presign, owner-scoped resolve endpoint, attach consumer</name>
  <files>CoffeePeek.Contract/Dtos/ResolvedUploadedPhotoDto.cs, CoffeePeek.Contract/Events/Photo/CheckInPhotosAttachedEvent.cs, CoffeePeek.MediaService/Controllers/PhotosController.cs, CoffeePeek.MediaService/Commands/ResolveUploadedPhotosQuery.cs, CoffeePeek.MediaService/Handlers/ResolveUploadedPhotosHandler.cs, CoffeePeek.MediaService/Consumers/CheckInPhotosAttachedHandler.cs, CoffeePeek.MediaService/Repositories/IPhotoRepository.cs, CoffeePeek.MediaService/Repositories/PhotoRepository.cs, CoffeePeek.MediaService/Services/Interfaces/IStorageService.cs, CoffeePeek.MediaService/Services/MinIOStorageService.cs, CoffeePeek.MediaService/Internal/Generated/WolverineHandlers/*, CoffeePeek.MediaService.Tests/CoffeePeek.MediaService.Tests.csproj, CoffeePeek.MediaService.Tests/PhotosControllerTests.cs, CoffeePeek.MediaService.Tests/ResolveUploadedPhotosHandlerTests.cs, CoffeePeek.MediaService.Tests/CheckInPhotosAttachedHandlerTests.cs</files>
  <behavior>
    - PhotosController.GenerateReviewUploadUrls sends GenerateShopPhotosCommand with OwnerId = userContext user and StorageKeyPrefix "reviews"; GenerateUploadUrls (/shop) still sends a null prefix.
    - ResolveUploadedPhotosHandler returns only keys whose metadata has OwnerId == caller, BucketType Shop, Status Pending, !IsExpired, and whose object exists in storage; SizeBytes comes from storage (the real object), FileName/ContentType from metadata.
    - Foreign-owner, Confirmed, expired, and not-uploaded keys are omitted (not errors); an empty or over-limit key list (more than 20) returns Response.Error.
    - CheckInPhotosAttachedHandler calls MarkAsPermanent(key, BucketType.Shop) and sets Status Confirmed for each Pending photo owned by event.UserId, skips others, saves once, and is idempotent (a second delivery does nothing).
  </behavior>
  <action>
Contract (additive only, so no versioning needed):
- Add `public record ResolvedUploadedPhotoDto(string StorageKey, string FileName, string ContentType, long SizeBytes);` next to UploadedPhotoDto (namespace CoffeePeek.Contract.Dtos).
- Add `public record CheckInPhotosAttachedEvent(Guid UserId, Guid CheckInId, IReadOnlyCollection<string> StorageKeys, DateTime AttachedAt);` in Events/Photo (namespace CoffeePeek.Contract.Events).

Storage: add `Task<long?> GetObjectSize(string storageKey, BucketType bucketType, CancellationToken ct = default)` to IStorageService. Implement it in MinIOStorageService with the same StatObjectAsync + `catch (MinioException)` shape as Exists, returning `stat.Size`, or null when the object is missing. Do not change Exists.

Repository: add `Task<List<PhotoMetadata>> GetByStorageKeysAsync(Guid ownerId, IReadOnlyCollection<string> storageKeys, CancellationToken ct = default)` that filters `OwnerId == ownerId && storageKeys.Contains(StorageKey)`.

Resolve: add `public record ResolveUploadedPhotosQuery(IReadOnlyCollection<string> StorageKeys) { [JsonIgnore] public Guid OwnerId { get; init; } }`. Add a static `ResolveUploadedPhotosHandler.Handle(query, IPhotoRepository, IStorageService, ct)` returning `Response<List<ResolvedUploadedPhotoDto>>`:
- Reject an empty list or more than 20 keys with Response.Error. 20 is a hard sanity cap; Shops enforces the real limit.
- Load the photos via GetByStorageKeysAsync(query.OwnerId, distinct keys).
- Filter in memory as well: OwnerId matches, `BucketType == BucketType.Shop`, `Status == PhotoStatus.Pending`, `!IsExpired`. The in-memory filter is defense in depth and keeps the handler unit-testable with a mocked repository.
- For each remaining photo, call GetObjectSize; skip it if null. Map to the DTO using the real size.
- Do not mutate anything here. The check-in may still fail after resolve.

Controller:
- Add `[HttpPost("resolve")] ResolveUploadedPhotos([FromBody] ResolveUploadedPhotosQuery query, ct)`. It sets `query with { OwnerId = userContext.GetUserIdOrThrow() }`, invokes via bus, and returns Ok. Owner always comes from the principal, never the body.
- Add `[HttpPost("review")] GenerateReviewUploadUrls(List<PhotoRequest>, ct)`, mirroring /menu with prefix "reviews". Add XML doc comments and ProducesResponseType attributes like the siblings.
- No gateway change is needed: route `media-Photos-route` matches `/api/Photos/{**remainder}` and YARP path matching is case-insensitive. Confirm by reading CoffeePeek.Gateway/appsettings.json.

Consumer: add `CheckInPhotosAttachedHandler(IPhotoRepository, IUnitOfWork, IStorageService)` in Consumers, using the DeletePhotoHandler shape: instance class, primary constructor, `Handle(CheckInPhotosAttachedEvent message, CancellationToken ct)`.
- Load via GetByStorageKeysAsync(message.UserId, message.StorageKeys).
- For each photo with `Status == Pending`: call `await storageService.MarkAsPermanent(photo.StorageKey, BucketType.Shop, ct)`, then set `photo.Status = PhotoStatus.Confirmed`.
- Call `unitOfWork.SaveChangesAsync(ct)` once, and only when at least one photo changed.
- Mark storage before the status change: if MinIO throws, Wolverine retries and the photo is still Pending. MarkAsPermanent is idempotent.
- Leave ConfirmPhotoCommand/Handler untouched (out of scope).

Tests (MediaService.Tests):
- Add `<PackageReference Include="Moq" />` to the csproj (the version is central). Use Moq with IMessageBus/IUserContext for the controller test, and Moq for IPhotoRepository/IStorageService/IUnitOfWork.
- Cover each behavior bullet, one [Fact] each, with FluentAssertions.

Codegen: from CoffeePeek.MediaService, run `dotnet run -- codegen write` using the same in-memory config values the WolverinePreGeneratedHandlersTests test uses (pass them as env vars or command-line config). Commit the regenerated Internal/Generated/WolverineHandlers files.
  </action>
  <verify>
    <automated>dotnet test CoffeePeek.MediaService.Tests</automated>
  </verify>
  <done>All MediaService tests pass, including WolverinePreGeneratedHandlersTests and the new controller/resolve/consumer tests. /shop behaviour is unchanged.</done>
</task>

<task type="auto" tdd="true">
  <name>Task 2: Shops side: validate, resolve and attach check-in photos</name>
  <files>CoffeePeek.Shops.Domain/BussinessConstants.cs, CoffeePeek.Shops.Application/Abstractions/IUploadedPhotoLookup.cs, CoffeePeek.Shops.Application/ValidationStrategy/CheckIn/CheckInValidationStrategy.cs, CoffeePeek.Shops.Application/Features/CheckIn/CreateCheckIn/CreateCheckInHandler.cs, CoffeePeek.Shops.Infrastructure/Media/MediaUploadedPhotoLookup.cs, CoffeePeek.Shops.Infrastructure/DependencyInjection.cs, CoffeePeek.ShopsService/Internal/Generated/WolverineHandlers/*, CoffeePeek.Shops.Application.Tests/Features/CheckIn/CreateCheckInHandlerTests.cs, CoffeePeek.Shops.Application.Tests/Features/CheckIn/CheckInPhotoValidationTests.cs, CoffeePeek.Shops.Infrastructure.Tests/Media/MediaUploadedPhotoLookupTests.cs</files>
  <behavior>
    - The strategy rejects: more than MaxCheckInPhotos (5) photos with "A check-in can have at most 5 photos"; duplicate StorageKey (ordinal compare) with "Duplicate photos are not allowed"; a null or whitespace StorageKey, or one longer than 255, with "Photo storage key is required".
    - The handler rejects (ValidationException "One or more photos were not found, do not belong to you, or were already used") when the lookup returns fewer keys than requested (unknown or foreign key). In that case nothing is added to the repository and nothing is published.
    - The handler throws ValidationException "Photos could not be verified, please try again" when the lookup reports failure (Media unreachable). This fails closed.
    - The saved ShopPhoto and CheckinCreatedEvent.ReviewDto.Photos use the resolved FileName/ContentType/SizeBytes, not the client values (a test sends a spoofed ContentType "text/html" and Size 1 and asserts the resolved values win).
    - With photos, the handler publishes CheckInPhotosAttachedEvent(UserId, checkIn.Id, keys) for both public and private check-ins. Without photos it never calls the lookup and publishes no attach event.
    - MediaUploadedPhotoLookup posts to /api/Photos/resolve with the X-Gateway-Auth and X-User-Id headers, returns the resolved list on success, and returns a failure result on non-2xx or HttpRequestException/TaskCanceledException.
  </behavior>
  <action>
- Add `public const int MaxCheckInPhotos = 5;` next to MaxCheckInNoteLength in CoffeePeek.Shops.Domain/BussinessConstants.cs. Also add `public const int MaxCheckInPhotoStorageKeyLength = 255;` (this matches the Media PhotoMetadata.StorageKey MaxLength).

- Abstraction (CoffeePeek.Shops.Application/Abstractions/IUploadedPhotoLookup.cs): `Task<UploadedPhotoLookupResult> ResolveAsync(Guid userId, IReadOnlyCollection<string> storageKeys, CancellationToken ct = default)`. Declare `public record UploadedPhotoLookupResult(bool Succeeded, IReadOnlyList<ResolvedUploadedPhotoDto> Photos)` in the same file.

- Static checks: extend the private static Validate in CheckInValidationStrategy with the three photo checks, placed before the IsPublic block. They apply to public and private check-ins alike. Photo existence is NOT checked in the strategy, because the handler needs the resolved data and a second HTTP call would be wasteful.

- CreateCheckInHandler: add an `IUploadedPhotoLookup photoLookup` parameter before ct. After validation succeeds and when Photos has any items:
  - Call ResolveAsync(command.UserId, keys, ct). Throw the fail-closed ValidationException when !Succeeded.
  - Build a dictionary by StorageKey. If any requested key is missing, throw the "not found / not yours / already used" ValidationException.
  - Build ShopPhoto from the resolved values (`new ShopPhoto(r.FileName, r.ContentType, r.StorageKey, r.SizeBytes, command.UserId)`), keeping the client's order.
  - Build the trusted `UploadedPhotoDto[]` from the resolved values too, and use it for `ReviewDto.Photos` in CheckinCreatedEvent instead of `command.Photos`.
  - After `queryCheckInRepository.Add(checkIn)`, when there are photos, call `await bus.PublishAsync(new CheckInPhotosAttachedEvent(command.UserId, checkIn.Id, keys, DateTime.UtcNow))`. This sits before SaveChangesAsync, like the existing CheckinCreatedEvent, so the durable outbox only ships it on commit.

- Infrastructure: add MediaUploadedPhotoLookup in CoffeePeek.Shops.Infrastructure/Media/, copying the AccountUserExistenceLookup shape (IHttpClientFactory, IOptions<GatewayAuthOptions>, ILogger).
  - Use named client "media-photo-lookup" and `HttpMethod.Post "/api/Photos/resolve"` with a JsonContent body `{ storageKeys }`.
  - Headers: `request.AddGatewayAuthHeader(secret)` plus `request.Headers.TryAddWithoutValidation(GatewayHeaderConsts.XUserId, userId.ToString())`.
  - Deserialize `Response<List<ResolvedUploadedPhotoDto>>`.
  - Log a warning and return Succeeded=false on non-success status, IsSuccess false, or HttpRequestException/TaskCanceledException.
  - Register it in DependencyInjection.cs with `services.AddHttpClient("media-photo-lookup", c => c.BaseAddress = new Uri($"http://{AppResources.MediaService}"))`, following the account-user-lookup registration including any resilience or timeout it applies. Then add `services.AddScoped<IUploadedPhotoLookup, MediaUploadedPhotoLookup>()`.

- Tests:
  - Update CreateCheckInHandlerTests for the new parameter by adding a `Mock<IUploadedPhotoLookup>`. The existing `Handle_WithPhotos_AttachesPhotosRegardlessOfVisibility` must set up the lookup to resolve its keys.
  - Add the handler behavior tests to that file.
  - Put the strategy tests in CheckInPhotoValidationTests.cs. Mock the IQueryCoffeeShopRepository/IQueryCheckInRepository dependencies so the async path reaches Validate. The static checks run first, so the rejections return before the repositories are touched.
  - Put the lookup HTTP tests in Shops.Infrastructure.Tests/Media/MediaUploadedPhotoLookupTests.cs using a stub HttpMessageHandler. If Moq is missing from that csproj, add the central PackageReference, or use a hand-written stub handler.

- Codegen: from CoffeePeek.ShopsService, run `dotnet run -- codegen write`, using the config the ShopsService.Tests pre-generated handler test uses. Commit the output.
  </action>
  <verify>
    <automated>dotnet test CoffeePeek.Shops.Application.Tests && dotnet test CoffeePeek.Shops.Infrastructure.Tests && dotnet test CoffeePeek.ShopsService.Tests</automated>
  </verify>
  <done>Forged, foreign, unknown, duplicate and over-limit photos are rejected with 400 through ValidationException. The trusted metadata is persisted and forwarded to moderation. The attach event is published. All three Shops test projects pass.</done>
</task>

<task type="auto">
  <name>Task 3: Client and admin photo API docs</name>
  <files>docs/api/photos-client.md, docs/api/photos-admin.md</files>
  <action>
Create docs/api/ and match the style of docs/frontend-shop-photo-order.md: H1 title, base-URL line, Auth table, TypeScript `type` blocks, per-endpoint sections with an HTTP example, JSON request/response examples, and an errors table. Every value must come from the code as implemented in Tasks 1-2. Read the exact strings; do not paraphrase error messages.

photos-client.md must cover:
1. `POST /api/photos/review`, with `/shop` noted as the legacy alias.
   - Request is `PhotoRequest[]`: read the fields from CoffeePeek.MediaService/Commands/Base.
   - Response is `Response<GenerateUploadUrlResponse[]>` (photoId, uploadUrl, storageKey).
   - Rate limit: media-upload policy, 10/min.
2. `PUT {uploadUrl}` with the exact required headers `Content-Type: <same as requested>` and `x-amz-tagging: is_permanent=False`. Explain that a mismatch returns 403 SignatureDoesNotMatch, and give the 600 s URL expiry.
3. `POST /api/checkins` body, showing `photos: UploadedPhotoDto[]`.
   - State that only storageKey is trusted: fileName/contentType/size are ignored and replaced server-side.
   - Limits: MaxCheckInPhotos, no duplicates, photo must be uploaded by the same user, must be used within 24h, single use.
   - List all error messages from Task 2, with HTTP 400.
   - Add a note that check-ins without photos are unaffected.
4. What happens after save: photos become permanent. For public check-ins, the review goes to moderation with the photos.
5. A full worked example sequence (curl or HTTP blocks).

photos-admin.md must cover:
1. How review photos appear in moderation. Read CoffeePeek.ModerationService/Controllers/ModerationReviewsController.cs and the ModerationReview aggregate / Mapster config for the photo fields and URLs returned.
2. The approve/reject effect on photos. Read the approve and reject handlers and trace what happens to ReviewPhotos and Shops review photos on approval. Document the actual behavior only.
3. The admin and owner shop gallery endpoints in CoffeePeek.ShopsService/Controllers/AdminCoffeeShopsController.cs (POST/DELETE `{id}/photos`, PUT `{id}/photos/order`, menu photos) and OwnerCoffeeShopsController, including body types, the policy, and which presign endpoint to use for each (/shop, /menu, /roaster). Link to frontend-shop-photo-order.md for ordering instead of duplicating it.
4. The new `POST /api/photos/resolve` endpoint, documented as a service-to-service endpoint (caller-scoped, safe if called by clients).

If a behavior cannot be determined from the code, state it as "not currently supported" rather than inventing it.
  </action>
  <verify>
    <automated>test -f docs/api/photos-client.md && test -f docs/api/photos-admin.md && grep -q "x-amz-tagging" docs/api/photos-client.md && grep -q "api/photos/review" docs/api/photos-client.md && grep -qi "moderation" docs/api/photos-admin.md</automated>
  </verify>
  <done>Both docs exist and match the repository doc style. Every endpoint, header, limit and error string in them matches the code.</done>
</task>

</tasks>

<threat_model>
## Trust Boundaries

| Boundary | Description |
|----------|-------------|
| client -> Shops POST /api/checkins | Untrusted photo list (keys + metadata) |
| client -> Media POST /api/photos/* | Authenticated user requests presign / resolve |
| client -> MinIO presigned PUT | Untrusted bytes, constrained by signed headers |
| Shops -> Media HTTP | Service call carrying gateway secret + user id |
| Shops -> Media RabbitMQ | CheckInPhotosAttachedEvent |

## STRIDE Threat Register

| Threat ID | Category | Component | Disposition | Mitigation Plan |
|-----------|----------|-----------|-------------|-----------------|
| T-jpd-01 | Spoofing/Elevation | CreateCheckInHandler photos | mitigate | Every key is resolved against Media PhotoMetadata scoped to the caller's OwnerId; unknown or foreign keys give a 400 |
| T-jpd-02 | Tampering | UploadedPhotoDto ContentType/Size | mitigate | Replaced by metadata ContentType (signed at presign) and the real MinIO object size |
| T-jpd-03 | Tampering | Key reuse across check-ins | mitigate | Resolve returns only Pending photos; the attach consumer sets Confirmed; per-user lock + cooldown cover the async window |
| T-jpd-04 | Information disclosure | POST /api/photos/resolve exposed via gateway | mitigate | Owner is taken from the principal only; a caller can resolve only their own photos |
| T-jpd-05 | Denial of service | Photo count / resolve fan-out | mitigate | MaxCheckInPhotos=5 in Shops, a hard 20-key cap in Media; the existing media-upload rate limit covers public calls |
| T-jpd-06 | Denial of service | Media down blocks check-ins with photos | accept | Fails closed on purpose; check-ins without photos are unaffected |
| T-jpd-07 | Repudiation/Integrity | Attach event lost | mitigate | Published inside the durable outbox before SaveChanges; the consumer is idempotent |
</threat_model>

<verification>
- `dotnet build CoffeePeek.slnx` succeeds with no new warnings in touched projects.
- `dotnet test CoffeePeek.slnx` is green, including both WolverinePreGeneratedHandlers tests (Media, Shops).
- `git diff --stat CoffeePeek.Contract` shows only added files (no existing contract type modified).
- No EF migration was added (no schema change).
</verification>

<success_criteria>
- Check-in photo forgery, reuse, spoofed metadata, and unbounded counts are all rejected server-side, with a test for each.
- Attached photos are marked permanent and Confirmed in Media.
- POST /api/photos/review exists and /shop still works.
- docs/api/photos-client.md and docs/api/photos-admin.md are written from the actual code.
</success_criteria>

<output>
Create `.planning/quick/260924-jpd-harden-review-and-check-in-photo-attachm/260924-jpd-SUMMARY.md` when done
</output>
