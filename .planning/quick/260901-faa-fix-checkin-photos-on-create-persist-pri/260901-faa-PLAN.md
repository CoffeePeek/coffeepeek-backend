---
phase: quick-260901-faa
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - CoffeePeek.Shops.Domain/Aggregates/CheckInAggregate/CheckIn.cs
  - CoffeePeek.Shops.Application/Features/CheckIn/CreateCheckIn/CreateCheckInHandler.cs
  - CoffeePeek.Contract/Dtos/CoffeeShop/CheckInDto.cs
  - CoffeePeek.Shops.Application/Mapper/MapsterConfiguration.cs
  - CoffeePeek.Shops.Domain.Tests/Aggregates/CheckInAggregate/CheckInTests.cs
  - CoffeePeek.Shops.Application.Tests/Features/CheckIn/CreateCheckIn/CreateCheckInHandlerTests.cs
  - CoffeePeek.Shops.Application.Tests/Mapper/MapsterConfigurationCheckInTests.cs
autonomous: true
requirements: [QUICK-260901-faa]
must_haves:
  truths:
    - "Creating a check-in with IsPublic=false actually persists a row (queryCheckInRepository.Add is called for both private and public check-ins, not just public)"
    - "Creating a check-in without ever calling AssignRating no longer throws — CheckIn.Rating defaults to a zero-value Rating instead of null, satisfying EF's required owned-entity navigation"
    - "Photos submitted via CreateCheckInCommand.Photos are attached to the CheckIn aggregate regardless of IsPublic, and GET /api/CheckIns (CheckInDto.Photos) returns them with a working FullUrl"
  artifacts:
    - path: "CoffeePeek.Shops.Domain/Aggregates/CheckInAggregate/CheckIn.cs"
      provides: "Private constructor defaults Rating to a zero-value Rating instead of leaving it null"
      contains: "new Rating(0, 0, 0)"
    - path: "CoffeePeek.Shops.Application/Features/CheckIn/CreateCheckIn/CreateCheckInHandler.cs"
      provides: "queryCheckInRepository.Add(checkIn) called unconditionally, not only inside the IsPublic branch"
    - path: "CoffeePeek.Contract/Dtos/CoffeeShop/CheckInDto.cs"
      provides: "Photos property exposing uploaded check-in photos to API clients"
      contains: "ShortPhotoMetadataDto[] Photos"
    - path: "CoffeePeek.Shops.Application/Mapper/MapsterConfiguration.cs"
      provides: "CheckIn.ShopPhotos mapped into CheckInDto.Photos"
      contains: "dest.Photos, src => src.ShopPhotos"
  key_links:
    - from: "CoffeePeek.Shops.Persistance/Queries/CheckInQueries.cs"
      to: "CoffeePeek.Shops.Application/Mapper/MapsterConfiguration.cs"
      via: "ProjectToType<CheckInDto>(mapper.Config) uses the CheckIn -> CheckInDto TypeAdapterConfig, now including Photos"
      pattern: "NewConfig<CheckIn, CheckInDto>"
    - from: "CoffeePeek.Shops.Application/Features/CheckIn/CreateCheckIn/CreateCheckInHandler.cs"
      to: "CoffeePeek.Shops.Domain/Aggregates/CheckInAggregate/IQueryCheckInRepository.cs"
      via: "queryCheckInRepository.Add(checkIn) — now called for every check-in, not just public ones"
      pattern: "queryCheckInRepository\\.Add\\(checkIn\\)"
---

<objective>
Two bugs found while investigating a request to support photos on check-in creation:

**Bug 1 (persistence):** `CreateCheckInHandler.Handle` only calls `queryCheckInRepository.Add(checkIn)`
inside the `if (command.IsPublic)` branch. For a private check-in (`IsPublic == false`), the
`CheckIn` aggregate is built in memory (note/photos attached) but never added to the EF `DbSet`, so
`unitOfWork.SaveChangesAsync()` has nothing to persist — **private check-ins are silently dropped**.
The existing test `Handle_PrivateCheckIn_CreatesCheckInAndSaves` only asserts `SaveChangesAsync` was
called, not that anything was actually added, so it doesn't catch this.

**Why `Add()` was gated on `IsPublic` in the first place:** `CheckIn.Rating` (owned, EF-required
navigation — see the existing comment in the handler about Postgres rejecting inserts) is only ever
set via `AssignRating(...)`, which today only runs inside the `IsPublic` branch. `Rating` is a
non-nullable reference-type property on `CheckIn` that is never initialized in the private
constructor, so it defaults to `null`. If `Add()` is simply moved outside the `IsPublic` check
without also fixing this, every private check-in would start throwing at `SaveChangesAsync()`
instead of silently no-op'ing — a worse regression. Fix: give `CheckIn` a zero-value default
`Rating` (via `Rating`'s existing `internal` unvalidated constructor, accessible from `CheckIn`
since both live in `CoffeePeek.Shops.Domain`) so the owned navigation is never null. `AssignRating`
still overwrites it with the real 1-5 rating for public check-ins.

**Bug 2 / feature gap (read-side):** `CreateCheckInCommand.Photos` and `CheckIn.AddPhotos(...)`
already exist and the `ShopPhotos` table already has a working `CheckInId` shadow FK (EF
auto-discovered, migration already applied) — so photos submitted at creation time already persist
correctly once Bug 1 is fixed. But `CheckInDto` (the `GET /api/CheckIns` response) has no `Photos`
field, so a client can never see photos it just uploaded. Fix: add
`ShortPhotoMetadataDto[] Photos` to `CheckInDto` and map it from `CheckIn.ShopPhotos` in
`MapsterConfiguration.cs`, mirroring the exact existing pattern used for
`CoffeeShop.ShopPhotos -> ShortShopDto.Photos` (same `ShopPhoto -> ShortPhotoMetadataDto` nested
Mapster config, including the computed `FullUrl` built via `MediaStorageUrlBuilder`).

Output: private check-ins are actually saved, no regression for public check-ins, and check-in
photos (submitted at creation) come back in `GET /api/CheckIns`.
</objective>

<execution_context>
@$HOME/.claude/get-shit-done/workflows/execute-plan.md
@$HOME/.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@.planning/STATE.md
@CLAUDE.md

<interfaces>
From CoffeePeek.Shops.Domain/Aggregates/CheckInAggregate/CheckIn.cs (current — to be changed):
```csharp
public sealed partial class CheckIn : AggregateRoot<Guid>
{
    public string? Note { get; private set; }
    public Guid UserId { get; private set; }
    public Guid ShopId { get; private set; }
    public DateTime VisitedAt { get; private set; }
    public Guid? ReviewId { get; private set; }
    public Rating Rating { get; private set; }
    private readonly List<ShopPhoto> _shopPhotos = [];
    public IReadOnlyCollection<ShopPhoto> ShopPhotos => _shopPhotos.AsReadOnly();

    private CheckIn() { }

    private CheckIn(Guid userId, Guid shopId, DateTime visitedAt)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        ShopId = shopId;
        VisitedAt = visitedAt;
        CreatedAtUtc = DateTime.UtcNow;
    }
}
```

From CoffeePeek.Shops.Domain/Entities/Rating.cs (DO NOT MODIFY — the `internal` ctor is what to use):
```csharp
public record Rating
{
    public int Place { get; private set; }
    public int Service { get; private set; }
    public int Coffee { get; private set; }
    public decimal AverageRating { get; private set; }

    private Rating() { }

    internal Rating(int place, int service, int coffee)
    {
        Place = place; Service = service; Coffee = coffee;
        AverageRating = (Place + Service + Coffee) / 3m;
    }

    public static Rating Create(int place, int service, int coffee) { /* validates 1-5, throws DomainException otherwise */ }
    public void Update(int coffee, int place, int service) { ... }
}
```
`internal Rating(int, int, int)` bypasses the 1-5 range validation in `Create` — safe to use here
because a zero-value placeholder rating for a private/unrated check-in is intentional, not a
validation bug. Both `Rating` and `CheckIn` are in assembly `CoffeePeek.Shops.Domain`, so the
`internal` ctor is accessible.

From CoffeePeek.Shops.Application/Features/CheckIn/CreateCheckIn/CreateCheckInHandler.cs (current — to be changed):
```csharp
public static class CreateCheckInHandler
{
    public static async Task<Response<CreateCheckInResponse>> Handle(
        CreateCheckInCommand command,
        IQueryCheckInRepository queryCheckInRepository,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        IAsyncValidationStrategy<CreateCheckInCommand> validationStrategy,
        IMapper mapper,
        ICacheService cacheService,
        CancellationToken ct)
    {
        var validationResult = await validationStrategy.ValidateAsync(command, ct);
        if (!validationResult.IsValid)
            throw new ValidationException(validationResult.ErrorMessage!);

        var checkIn = Domain.Aggregates.CheckInAggregate.CheckIn.Create(
            command.UserId, command.CoffeeShopId, command.VisitedAt);

        if (!string.IsNullOrEmpty(command.Note))
            checkIn.UpdateNote(command.Note);

        if (command.Photos is { Count: > 0 })
        {
            var photos = command.Photos.Select(x =>
                new ShopPhoto(x.FileName, x.ContentType, x.StorageKey, x.Size, command.UserId));
            checkIn.AddPhotos(photos);
        }

        if (command.IsPublic)
        {
            // Owned Rating columns on CheckIns are NOT NULL — persist the public rating
            // before SaveChanges or Postgres rejects the insert as a generic CONFLICT.
            checkIn.AssignRating(command.Rating!.Place, command.Rating.Service, command.Rating.Coffee);
            queryCheckInRepository.Add(checkIn);  // <-- BUG: only reached when IsPublic is true

            try
            {
                var commentPreview = string.Join(" ",
                    command.Note?.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(3) ?? []);

                var review = Review.Create(
                    command.CoffeeShopId, command.UserId, command.UserName,
                    header: commentPreview, comment: command.Note!,
                    ratingPlace: command.Rating.Place, ratingService: command.Rating.Service, ratingCoffee: command.Rating.Coffee);

                await bus.PublishAsync(new CheckinCreatedEvent
                {
                    UserId = command.UserId,
                    ShopId = command.CoffeeShopId,
                    CreatedAt = checkIn.CreatedAtUtc,
                    ReviewDto = mapper.Map<ReviewDto>(review)
                });
            }
            catch (DomainException) { throw; }
        }

        await unitOfWork.SaveChangesAsync(ct);
        await PublicStatsCacheInvalidator.InvalidateAsync(cacheService, ct);

        return Response<CreateCheckInResponse>.Success(new CreateCheckInResponse(checkIn.Id));
    }
}
```

From CoffeePeek.Contract/Dtos/CoffeeShop/CheckInDto.cs (current — to be changed; `Photos` property is new):
```csharp
public class CheckInDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid ShopId { get; set; }
    public string Note { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public Guid? ReviewId { get; set; }
    public string ShopName { get; set; } = string.Empty;
}
```

From CoffeePeek.Contract/Dtos/ShortPhotoMetadataDto.cs (DO NOT MODIFY — reuse as-is):
```csharp
public class ShortPhotoMetadataDto
{
    public Guid Id { get; init; }
    public string FileName { get; init; }
    public string StorageKey { get; init; }
    public string FullUrl { get; init; }
    public int SortIndex { get; init; }
}
```
`CheckInDto` is in namespace `CoffeePeek.Contract.Dtos.CoffeeShop`, which is nested inside
`CoffeePeek.Contract.Dtos` — C# resolves `ShortPhotoMetadataDto` from the enclosing namespace
without needing an explicit `using` (same as `ShortShopDto.cs` in the same folder already does).

From CoffeePeek.Shops.Application/Mapper/MapsterConfiguration.cs (current — the two blocks to change; ~line 29 shows the exact pattern to mirror, ~line 75-78 is the block to edit):
```csharp
config.NewConfig<CoffeeShop, ShortShopDto>()
    .Map(dest => dest.Photos, src => src.ShopPhotos.OrderBy(p => p.SortIndex).ThenBy(p => p.CreatedAtUtc))
    ...

config.NewConfig<ShopPhoto, ShortPhotoMetadataDto>()
    .Map(dest => dest.FullUrl, src =>
        MediaStorageUrlBuilder.BuildPublicUrl(
            mediaOptions.PublicEndpoint,
            mediaOptions.ShopBucketName,
            src.StorageKey) ?? string.Empty);

config.NewConfig<CheckIn, CheckInDto>()
    // ShopName is set manually in handlers via repository
    .Ignore(dest => dest.ShopName)
    .Map(dest => dest.CreatedAt, src => src.CreatedAtUtc);   // added by a prior quick task — keep this line
```
The `ShopPhoto -> ShortPhotoMetadataDto` config is global (keyed by type, not by owner), so it
already applies to `CheckIn.ShopPhotos` items with zero extra work — you only need to add a
`.Map(dest => dest.Photos, src => src.ShopPhotos.OrderBy(p => p.SortIndex).ThenBy(p => p.CreatedAtUtc))`
line to the `CheckIn, CheckInDto` config, exactly mirroring the `CoffeeShop, ShortShopDto` line above.

From CoffeePeek.Shops.Persistance/Queries/CheckInQueries.cs (consumer — no change needed; `ProjectToType`
already handles correlated-collection projection for `ShortShopDto.Photos` in `CoffeeShopQueries.cs`
the exact same way, with no `.Include()`, so no query changes are needed here either):
```csharp
var items = await query
    .OrderByDescending(c => c.CreatedAtUtc)
    .Skip((pageNumber - 1) * pageSize)
    .Take(pageSize)
    .ProjectToType<CheckInDto>(mapper.Config)
    .ToArrayAsync(ct);
```

From CoffeePeek.Contract/Dtos/UploadedPhotoDto.cs (unchanged, for test construction):
```csharp
public record UploadedPhotoDto(string FileName, string ContentType, string StorageKey, long Size);
```

Existing regression test file to extend: `CoffeePeek.Shops.Application.Tests/Mapper/MapsterConfigurationCheckInTests.cs`
(created by a prior quick task — already has one test, `Adapt_CheckInToCheckInDto_MapsCreatedAtFromCreatedAtUtc`,
using `MapsterConfiguration.CreateConfig(new MediaPublicUrlOptions())` and `checkIn.Adapt<CheckInDto>(config)`).
Add a new `[Fact]` to this same file/class rather than creating a new file.

Existing test file to extend: `CoffeePeek.Shops.Application.Tests/Features/CheckIn/CreateCheckIn/CreateCheckInHandlerTests.cs`
— has a `BuildCommand(bool isPublic, RatingDto? rating, string note)` helper and mocks
(`_checkInRepoMock`, `_unitOfWorkMock`, `_busMock`, `_validationMock`, `_mapperMock`, `_cacheMock`).
`Handle_PrivateCheckIn_CreatesCheckInAndSaves` currently only asserts `SaveChangesAsync` was called
and `PublishAsync` was never called — extend it to also verify `_checkInRepoMock.Verify(r =>
r.Add(It.IsAny<DomainCheckIn>()), Times.Once)` (this is the regression assertion for Bug 1).

Existing test file to extend: `CoffeePeek.Shops.Domain.Tests/Aggregates/CheckInAggregate/CheckInTests.cs`
— has `[Fact] Create_WithValidData_ReturnsCheckInWithAllFields` as the pattern to follow for a new
`[Fact]` asserting the default zero-value `Rating`.
</interfaces>
</context>

<tasks>

<task type="auto" tdd="true">
  <name>Task 1: Default Rating to zero-value in CheckIn constructor (domain fix)</name>
  <files>
    CoffeePeek.Shops.Domain/Aggregates/CheckInAggregate/CheckIn.cs,
    CoffeePeek.Shops.Domain.Tests/Aggregates/CheckInAggregate/CheckInTests.cs
  </files>
  <behavior>
    `CheckIn.Create(userId, shopId, visitedAt).Rating` is never null — it defaults to a `Rating`
    with `Place = 0, Service = 0, Coffee = 0, AverageRating = 0m`. `AssignRating(...)` still
    overwrites it correctly afterward (existing `AssignRating_WithValidScores_SetsOwnedRating` test
    must keep passing unchanged).
  </behavior>
  <action>
    In `CheckIn.cs`, in the private constructor `CheckIn(Guid userId, Guid shopId, DateTime visitedAt)`,
    add `Rating = new Rating(0, 0, 0);` (using `Rating`'s `internal` unvalidated constructor — this
    compiles because `CheckIn` and `Rating` are both in `CoffeePeek.Shops.Domain`). Add a one-line
    comment explaining why: the owned `Rating` navigation is EF-required, so every `CheckIn` needs a
    non-null placeholder until `AssignRating` sets the real value for public check-ins.

    In `CheckInTests.cs`, add `Create_WithValidData_HasDefaultZeroRating`: create a `CheckIn` via
    `CheckIn.Create(...)` and assert `checkIn.Rating` is not null and
    `checkIn.Rating.Place`/`.Service`/`.Coffee` are all `0`.
  </action>
  <verify>
    <automated>dotnet test CoffeePeek.Shops.Domain.Tests --filter FullyQualifiedName~CheckInTests</automated>
  </verify>
  <done>All CheckInTests pass, including the new default-Rating test and the existing AssignRating test.</done>
</task>

<task type="auto" tdd="true">
  <name>Task 2: Always persist check-ins (fix the IsPublic-gated Add) and attach photos regardless of visibility</name>
  <files>
    CoffeePeek.Shops.Application/Features/CheckIn/CreateCheckIn/CreateCheckInHandler.cs,
    CoffeePeek.Shops.Application.Tests/Features/CheckIn/CreateCheckIn/CreateCheckInHandlerTests.cs
  </files>
  <behavior>
    - Creating a check-in with `IsPublic: false` calls `queryCheckInRepository.Add(checkIn)` exactly
      once (today it is never called for private check-ins — this is the regression the new
      assertion catches).
    - Creating a check-in with `IsPublic: true` still calls `Add` exactly once, still assigns the
      rating, still publishes the `CheckinCreatedEvent`, and still throws `DomainException` for an
      invalid rating without calling `SaveChangesAsync` (existing behavior — must not regress).
    - Photos passed via `CreateCheckInCommand.Photos` end up in the saved `CheckIn.ShopPhotos`
      collection regardless of `IsPublic`.
  </behavior>
  <action>
    In `CreateCheckInHandler.cs`, move `queryCheckInRepository.Add(checkIn);` out of the
    `if (command.IsPublic)` block to run unconditionally — place it right after the photo-attachment
    block and before the `if (command.IsPublic)` block. Leave `checkIn.AssignRating(...)`, the review
    creation, and the `CheckinCreatedEvent` publish inside the `IsPublic` block exactly as they are
    today (only the `Add` call moves). Keep the existing comment about the NOT NULL owned Rating
    columns near `AssignRating` — it's still accurate context for why rating assignment must happen
    before `SaveChangesAsync`, it's just no longer the reason `Add` was conditional.

    In `CreateCheckInHandlerTests.cs`:
    - Update `Handle_PrivateCheckIn_CreatesCheckInAndSaves` to also assert
      `_checkInRepoMock.Verify(r => r.Add(It.IsAny<DomainCheckIn>()), Times.Once);` (the regression
      assertion for Bug 1).
    - Add `Handle_WithPhotos_AttachesPhotosRegardlessOfVisibility`: build a private command
      (`BuildCommand(isPublic: false)`) with `Photos` set via `with { Photos = [...] }` to a list of
      2 `UploadedPhotoDto` (distinct `FileName`/`StorageKey` values), capture the saved `CheckIn` via
      the `_checkInRepoMock.Setup(r => r.Add(...)).Callback<DomainCheckIn>(c => saved = c)` pattern
      already used in `Handle_PublicCheckIn_WithValidRating_PublishesEventAndSaves`, and assert
      `saved!.ShopPhotos` has count 2 with the expected `StorageKey` values.
  </action>
  <verify>
    <automated>dotnet test CoffeePeek.Shops.Application.Tests --filter FullyQualifiedName~CreateCheckInHandlerTests</automated>
  </verify>
  <done>All CreateCheckInHandlerTests pass, including the two new/updated assertions. No existing test's behavior changes except the two named above.</done>
</task>

<task type="auto" tdd="true">
  <name>Task 3: Expose check-in photos in CheckInDto via Mapster</name>
  <files>
    CoffeePeek.Contract/Dtos/CoffeeShop/CheckInDto.cs,
    CoffeePeek.Shops.Application/Mapper/MapsterConfiguration.cs,
    CoffeePeek.Shops.Application.Tests/Mapper/MapsterConfigurationCheckInTests.cs
  </files>
  <behavior>
    Adapting a `CheckIn` with photos attached (via `AddPhotos`) to `CheckInDto` through the real
    `MapsterConfiguration.CreateConfig(...)` produces a `CheckInDto.Photos` array with matching
    `StorageKey` values and a correctly built `FullUrl` (using the same `MediaStorageUrlBuilder`
    logic already proven for shop photos).
  </behavior>
  <action>
    In `CheckInDto.cs`, add `public ShortPhotoMetadataDto[] Photos { get; set; } = [];` (no new
    `using` needed — see interfaces note on namespace nesting).

    In `MapsterConfiguration.cs`, add
    `.Map(dest => dest.Photos, src => src.ShopPhotos.OrderBy(p => p.SortIndex).ThenBy(p => p.CreatedAtUtc))`
    to the existing `config.NewConfig<CheckIn, CheckInDto>()` chain (alongside the existing
    `.Ignore(dest => dest.ShopName)` and `.Map(dest => dest.CreatedAt, src => src.CreatedAtUtc)`).

    In `MapsterConfigurationCheckInTests.cs`, add `Adapt_CheckInToCheckInDto_MapsPhotosWithFullUrl`:
    build `MapsterConfiguration.CreateConfig(new MediaPublicUrlOptions { PublicEndpoint =
    "https://media.coffeepeek.by" })`, create a `CheckIn` via `CheckIn.Create(...)`, call
    `checkIn.AddPhotos([new ShopPhoto("photo.jpg", "image/jpeg", "checkins/photo.jpg", 1024,
    checkIn.UserId)])`, adapt to `CheckInDto`, and assert `dto.Photos` has length 1, `dto.Photos[0].StorageKey`
    equals `"checkins/photo.jpg"`, and `dto.Photos[0].FullUrl` equals
    `$"https://media.coffeepeek.by/{mediaOptions.ShopBucketName}/checkins/photo.jpg"` (reference the
    `mediaOptions.ShopBucketName` value in the assertion rather than hardcoding the bucket name).
  </action>
  <verify>
    <automated>dotnet test CoffeePeek.Shops.Application.Tests --filter FullyQualifiedName~MapsterConfigurationCheckInTests</automated>
  </verify>
  <done>Both MapsterConfigurationCheckInTests pass (the pre-existing CreatedAt test and the new Photos test).</done>
</task>

</tasks>

<verification>
1. `dotnet build CoffeePeek.slnx` succeeds with zero errors.
2. `dotnet test CoffeePeek.Shops.Domain.Tests --filter FullyQualifiedName~CheckInTests` passes.
3. `dotnet test CoffeePeek.Shops.Application.Tests --filter FullyQualifiedName~CreateCheckInHandlerTests` passes.
4. `dotnet test CoffeePeek.Shops.Application.Tests --filter FullyQualifiedName~MapsterConfigurationCheckInTests` passes.
5. Full `CoffeePeek.Shops.Application.Tests` and `CoffeePeek.Shops.Domain.Tests` suites pass (no regressions).
</verification>

<success_criteria>
- Private check-ins are persisted (repository.Add is called for every check-in, not only public ones).
- No regression to public check-in behavior (rating assignment, review creation, event publish, DomainException propagation).
- `CheckIn.Rating` is never null, satisfying the EF-required owned navigation without needing `AssignRating` first.
- Photos submitted on check-in creation are attached to the aggregate and returned by `GET /api/CheckIns` via `CheckInDto.Photos`.
- `CheckInDto.cs`, `ShortPhotoMetadataDto.cs`, and `CheckInQueries.cs` query shape need no other contract-breaking changes.
</success_criteria>

<output>
Create `.planning/quick/260901-faa-fix-checkin-photos-on-create-persist-pri/260901-faa-SUMMARY.md` when done
</output>
