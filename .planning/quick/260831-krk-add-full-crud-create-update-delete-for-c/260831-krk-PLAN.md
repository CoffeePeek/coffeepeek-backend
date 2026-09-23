---
phase: quick-260831-krk
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - CoffeePeek.Shops.Domain/BussinessConstants.cs
  - CoffeePeek.Shops.Domain/Aggregates/CoffeeShopAggregate/Entities/City.cs
  - CoffeePeek.Shops.Domain/Aggregates/CoffeeShopAggregate/Entities/CoffeeBean.cs
  - CoffeePeek.Shops.Domain/Aggregates/CoffeeShopAggregate/Entities/Roaster.cs
  - CoffeePeek.Shops.Domain/Aggregates/CoffeeShopAggregate/Entities/Equipment.cs
  - CoffeePeek.Shops.Domain/Aggregates/BrewMethods/BrewMethod.cs
  - CoffeePeek.Shops.Domain/Aggregates/CoffeeShopAggregate/Repositories/ICityRepository.cs
  - CoffeePeek.Shops.Domain/Aggregates/CoffeeShopAggregate/Repositories/ICoffeeBeanRepository.cs
  - CoffeePeek.Shops.Domain/Aggregates/CoffeeShopAggregate/Repositories/IRoasterRepository.cs
  - CoffeePeek.Shops.Domain/Aggregates/CoffeeShopAggregate/Repositories/IEquipmentRepository.cs
  - CoffeePeek.Shops.Domain/Aggregates/CoffeeShopAggregate/Repositories/IBrewMethodRepository.cs
  - CoffeePeek.Shops.Domain.Tests/Entities/CoffeeShopAggregate/CityTests.cs
  - CoffeePeek.Shops.Domain.Tests/Entities/CoffeeShopAggregate/CoffeeBeanTests.cs
  - CoffeePeek.Shops.Domain.Tests/Entities/CoffeeShopAggregate/RoasterTests.cs
  - CoffeePeek.Shops.Domain.Tests/Entities/CoffeeShopAggregate/EquipmentTest.cs
  - CoffeePeek.Shops.Domain.Tests/Aggregates/BrewMethods/BrewMethodTests.cs
  - CoffeePeek.Shops.Persistance/Repositories/QueryCityRepository.cs
  - CoffeePeek.Shops.Persistance/Repositories/QueryCoffeeBeanRepository.cs
  - CoffeePeek.Shops.Persistance/Repositories/QueryRoasterRepository.cs
  - CoffeePeek.Shops.Persistance/Repositories/QueryEquipmentRepository.cs
  - CoffeePeek.Shops.Persistance/Repositories/QueryBrewMethodRepository.cs
  - CoffeePeek.Shops.Persistance/DependencyInjection.cs
  - CoffeePeek.Shops.Application/Features/Admin/Catalogs/Cities/AdminCityHandlers.cs
  - CoffeePeek.Shops.Application/Features/Admin/Catalogs/Beans/AdminCoffeeBeanHandlers.cs
  - CoffeePeek.Shops.Application/Features/Admin/Catalogs/Roasters/AdminRoasterHandlers.cs
  - CoffeePeek.Shops.Application.Tests/Features/Admin/Catalogs/Cities/AdminCityHandlersTests.cs
  - CoffeePeek.Shops.Application.Tests/Features/Admin/Catalogs/Beans/AdminCoffeeBeanHandlersTests.cs
  - CoffeePeek.Shops.Application.Tests/Features/Admin/Catalogs/Roasters/AdminRoasterHandlersTests.cs
  - CoffeePeek.Contract/Enums/BrewMethodCategoryEnum.cs
  - CoffeePeek.Contract/Dtos/Shop/BrewMethodDto.cs
  - CoffeePeek.Shops.Application/Mapper/MapsterConfiguration.cs
  - CoffeePeek.Shops.Application/Features/Admin/Catalogs/BrewMethods/AdminBrewMethodHandlers.cs
  - CoffeePeek.Shops.Application/Features/Admin/Catalogs/Equipments/AdminEquipmentHandlers.cs
  - CoffeePeek.Shops.Application.Tests/Features/Admin/Catalogs/BrewMethods/AdminBrewMethodHandlersTests.cs
  - CoffeePeek.Shops.Application.Tests/Features/Admin/Catalogs/Equipments/AdminEquipmentHandlersTests.cs
  - CoffeePeek.ShopsService/Controllers/AdminCitiesController.cs
  - CoffeePeek.ShopsService/Controllers/AdminBeansController.cs
  - CoffeePeek.ShopsService/Controllers/AdminRoastersController.cs
  - CoffeePeek.ShopsService/Controllers/AdminBrewMethodsController.cs
  - CoffeePeek.ShopsService/Controllers/AdminEquipmentsController.cs
  - CoffeePeek.ShopsService/Controllers/AdminShopTagsController.cs
autonomous: true
requirements: ["QUICK-260831-krk"]

must_haves:
  truths:
    - "An Admin or Moderator can POST a new city/bean/equipment/roaster/brew-method/shop-tag and it appears in the matching GET /api/catalogs/* list"
    - "An Admin or Moderator can PATCH an existing catalog entry and the change is visible on the next GET /api/catalogs/* call (after cache invalidation)"
    - "An Admin or Moderator can DELETE a catalog entry and it no longer appears on GET /api/catalogs/*"
    - "Requests without Admin or Moderator role are rejected (403) on all six admin catalog endpoints, including shop-tags"
    - "Duplicate names return 409 Conflict; blank/too-long names or an invalid equipment category return 400"
    - "No EF Core migration is required — no new columns or tables are introduced"
  artifacts:
    - path: "CoffeePeek.Shops.Domain/Aggregates/CoffeeShopAggregate/Entities/City.cs"
      provides: "City.Update(string name) mutator with validation"
    - path: "CoffeePeek.Shops.Domain/Aggregates/CoffeeShopAggregate/Entities/CoffeeBean.cs"
      provides: "Public CoffeeBean(string name) constructor + Update(string name)"
    - path: "CoffeePeek.Shops.Domain/Aggregates/CoffeeShopAggregate/Entities/Roaster.cs"
      provides: "Public Roaster(string name) constructor + Update(string name)"
    - path: "CoffeePeek.Shops.Domain/Aggregates/BrewMethods/BrewMethod.cs"
      provides: "Public BrewMethod(string name, BrewMethodCategory category) constructor + Update(...)"
    - path: "CoffeePeek.Shops.Domain/Aggregates/CoffeeShopAggregate/Entities/Equipment.cs"
      provides: "Equipment.Update(string brand, string modelName, EquipmentCategory category)"
    - path: "CoffeePeek.Shops.Application/Features/Admin/Catalogs/Cities/AdminCityHandlers.cs"
      provides: "CreateCityCommand/UpdateCityCommand/DeleteCityCommand + handlers"
    - path: "CoffeePeek.Shops.Application/Features/Admin/Catalogs/Beans/AdminCoffeeBeanHandlers.cs"
      provides: "Create/Update/DeleteCoffeeBeanCommand + handlers"
    - path: "CoffeePeek.Shops.Application/Features/Admin/Catalogs/Roasters/AdminRoasterHandlers.cs"
      provides: "Create/Update/DeleteRoasterCommand + handlers"
    - path: "CoffeePeek.Shops.Application/Features/Admin/Catalogs/BrewMethods/AdminBrewMethodHandlers.cs"
      provides: "Create/Update/DeleteBrewMethodCommand + handlers"
    - path: "CoffeePeek.Shops.Application/Features/Admin/Catalogs/Equipments/AdminEquipmentHandlers.cs"
      provides: "Create/Update/DeleteEquipmentCommand + handlers"
    - path: "CoffeePeek.ShopsService/Controllers/AdminCitiesController.cs"
      provides: "POST/PATCH/DELETE /api/admin/cities"
    - path: "CoffeePeek.ShopsService/Controllers/AdminBeansController.cs"
      provides: "POST/PATCH/DELETE /api/admin/beans"
    - path: "CoffeePeek.ShopsService/Controllers/AdminRoastersController.cs"
      provides: "POST/PATCH/DELETE /api/admin/roasters"
    - path: "CoffeePeek.ShopsService/Controllers/AdminBrewMethodsController.cs"
      provides: "POST/PATCH/DELETE /api/admin/brew-methods"
    - path: "CoffeePeek.ShopsService/Controllers/AdminEquipmentsController.cs"
      provides: "POST/PATCH/DELETE /api/admin/equipments"
  key_links:
    - from: "CoffeePeek.ShopsService/Controllers/Admin*.cs"
      to: "CoffeePeek.Shops.Application/Features/Admin/Catalogs/**/*Handlers.cs"
      via: "IMessageBus.InvokeAsync(command, ct)"
      pattern: "bus\\.InvokeAsync<"
    - from: "CoffeePeek.Shops.Application/Features/Admin/Catalogs/**/*Handlers.cs"
      to: "CoffeePeek.Shops.Persistance/Repositories/*.cs"
      via: "I{Entity}Repository DI injection"
      pattern: "I(City|CoffeeBean|Roaster|BrewMethod|Equipment)Repository"
    - from: "CoffeePeek.ShopsService/Controllers/Admin*.cs"
      to: "CoffeePeek.Shared.Auth/Constants/RoleConsts.cs"
      via: "[Authorize(Policy = RoleConsts.Moderator)] (RequireRole(Moderator, Admin))"
      pattern: "Authorize\\(Policy = RoleConsts\\.Moderator\\)"
---

<objective>
Add full CRUD (Create, Update, Delete alongside the existing GET) for the six Shops-service catalog reference types — cities, beans, equipments, roasters, brew-methods, shop-tags — restricted to Admin and Moderator roles, following the exact layered DDD + CQRS/Wolverine pattern already established by `AdminShopTagHandlers.cs` / `AdminShopTagsController.cs` (the one catalog-shaped entity in this codebase that already has full CRUD).

Purpose: today `CatalogsController` only exposes GET for these six types; there is no code path anywhere in the repo that can create a City, CoffeeBean, Roaster, BrewMethod, or Equipment row (confirmed by search — zero constructors/call sites). Admins/Moderators need to manage these catalogs without direct DB access.

Output:
- Domain: mutator/constructor methods on City, CoffeeBean, Roaster, BrewMethod, Equipment; five new write-repository interfaces.
- Persistence: five new write-repository implementations + DI registration.
- Application: Create/Update/Delete commands+handlers for all five entities lacking them (ShopTag already has these), reusing existing Contract DTOs.
- API: five new `Admin*Controller` classes at `/api/admin/{cities|beans|roasters|brew-methods|equipments}`, all `[Authorize(Policy = RoleConsts.Moderator)]` (this policy already grants both Moderator and Admin roles). `AdminShopTagsController`'s policy is widened from Admin-only to the same Moderator policy so all six catalog types share identical authorization.
- Tests: domain unit tests (xUnit v3 + FluentAssertions) for new entity behavior, and Application handler tests (xUnit v3 + Moq + FluentAssertions) for every new Create/Update/Delete handler.
- No EF Core migration: no new columns/tables. `BrewMethod.Category` already exists as a DB column (`integer`, confirmed in `ShopsDbContextModelSnapshot.cs`) but was never settable from code — this plan makes it settable, not adds it.
</objective>

<execution_context>
@$HOME/.claude/get-shit-done/workflows/execute-plan.md
@$HOME/.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@CLAUDE.md
@CoffeePeek.ShopsService/Controllers/CatalogsController.cs
@CoffeePeek.Shops.Application/Features/Admin/ShopTags/AdminShopTagHandlers.cs
@CoffeePeek.ShopsService/Controllers/AdminShopTagsController.cs
@CoffeePeek.Shops.Persistance/Repositories/ShopTagRepository.cs
@CoffeePeek.Shared.Kernel/Response/Response.cs
@CoffeePeek.Shared.Kernel/Response/ResponseGeneric.cs
@CoffeePeek.Shared.Domain/Interfaces/Infrastructure/CacheKey.cs
@CoffeePeek.Shared.Kernel/Exceptions/DomainException.cs
@CoffeePeek.Gateway/Extensions/AuthExtensions.cs

<interfaces>
<!-- The precedent this plan mirrors exactly. Executor should copy this shape, not invent a new one. -->

From CoffeePeek.Shops.Application/Features/Admin/ShopTags/AdminShopTagHandlers.cs:
- `public record CreateShopTagCommand(string Slug, string Name, string? Description, int SortOrder = 0);`
- `public static class CreateShopTagHandler { public static async Task<Response<AdminShopTagDto>> Handle(CreateShopTagCommand command, IShopTagRepository repository, IUnitOfWork unitOfWork, ICacheService cacheService, CancellationToken ct) }` — checks duplicate via `repository.GetBySlugAsync`, returns `Response<T>.Error(HttpStatusCode.Conflict, ...)` on duplicate, else constructs the entity via its domain factory, `repository.Add(entity)`, `unitOfWork.SaveChangesAsync(ct)`, cache invalidation, `Response<T>.Success(dto)`.
- `public record UpdateShopTagCommand([property: JsonIgnore] Guid Id, string Name, string? Description, int SortOrder, bool IsActive);` — Id is `[JsonIgnore]` because the controller builds the command manually from route `id` + a body request record; it is never bound from JSON directly.
- Cache invalidation is a single internal static helper (`InvalidateTagCachesAsync`) on the Create handler class, reused by Update/Delete — it removes both the entity's own list-cache pattern AND `CacheKey.Shop.SearchPattern()` (shop search results embed catalog names).
- Delete-style handler (`DeactivateShopTagHandler`) returns `Response` (non-generic): `Response.Error((int)HttpStatusCode.NotFound, "...")` / `Response.Success(message: "...")`.

From CoffeePeek.ShopsService/Controllers/AdminShopTagsController.cs:
- `[ApiController] [Route("api/admin/shop-tags")] [Authorize(Policy = RoleConsts.Admin)] [Tags("Admin")]` — this plan changes this controller's policy to `RoleConsts.Moderator`.
- `[HttpPost] Create([FromBody] CreateShopTagCommand command, ct)` → `bus.InvokeAsync<Response<AdminShopTagDto>>(command, ct)`, `response.StatusCode switch { StatusCodes.Status409Conflict => Conflict(response), _ => BadRequest(response) }` on failure.
- `[HttpPatch("{id:guid}")] Update(Guid id, [FromBody] UpdateShopTagRequest request, ct)` — controller builds `new UpdateShopTagCommand(id, request.Name, ...)`, returns `Ok`/`NotFound`.
- `[HttpDelete("{id:guid}")] Deactivate(Guid id, ct)` → `bus.InvokeAsync<Response<AdminShopTagDto>>(new DeactivateShopTagCommand(id), ct)`.

Confirmed via `CoffeePeek.Gateway/Extensions/AuthExtensions.cs` AND `CoffeePeek.ShopsService/DependencyInjection.cs` (both declare identical policies):
`.AddPolicy(RoleConsts.Moderator, policy => policy.RequireRole(RoleConsts.Moderator, RoleConsts.Admin))` — the `Moderator` policy already grants Admin too. Every new controller in this plan uses `[Authorize(Policy = RoleConsts.Moderator)]` — do NOT invent a combined policy.

Response static factories confirmed in CoffeePeek.Shared.Kernel/Response/*.cs:
- `Response<TData>.Success(TData data, string message = null)`
- `Response<TData>.Error(HttpStatusCode statusCode, string message)` (needs `using System.Net;`)
- `Response.Success(object? data = null, string? message = null)`
- `Response.Error(int statusCode, string message)`

CacheKey (CoffeePeek.Shared.Domain/Interfaces/Infrastructure/CacheKey.cs) already has every pattern needed — do not add new cache keys:
`CacheKey.City.ListPattern()`, `CacheKey.CoffeeBean.ListPattern()`, `CacheKey.Roaster.ListPattern()`, `CacheKey.Equipment.ListPattern()`, `CacheKey.BrewMethod.ListPattern()`, `CacheKey.Shop.SearchPattern()`.

Existing catalog GET DTOs to reuse as-is (no new Admin-only DTOs needed — unlike ShopTag, none of these five entities have admin-only fields):
- `CoffeePeek.Contract.Dtos.Internal.CityDto { Guid Id; string Name; }`
- `CoffeePeek.Contract.Dtos.Shop.CoffeeBeansDto { Guid Id; string Name; }`
- `CoffeePeek.Contract.Dtos.Shop.RoasterDto { Guid Id; string Name; }`
- `CoffeePeek.Contract.Dtos.Shop.BrewMethodDto { Guid Id; string Name; }` — this plan adds a `Category` field (additive, non-breaking).
- `CoffeePeek.Contract.Dtos.Shop.EquipmentDto { Guid Id; string Name; string Brand; string Model; EquipmentCategoryEnum Category; }` — already has an explicit Mapster config (`Model` ← `ModelName`, `Category` ← `(EquipmentCategoryEnum)CategoryId`); no Mapster change needed for Equipment.

IMapper (Mapster) already maps `City→CityDto`, `CoffeeBean→CoffeeBeansDto`, `Roaster→RoasterDto` by pure name-convention (Id/Name match) with zero explicit config — confirmed by the existing `GetAllCitiesHandler`/`GetAllBeansHandler`/`GetAllRoastersHandler`, which call `mapper.Map<T[]>(entities)` with no `NewConfig<>` entry for these three. New Create/Update handlers can call `mapper.Map<TDto>(entity)` the same way.
</interfaces>
</context>

<tasks>

<task type="auto" tdd="true">
  <name>Task 1: Domain — make City, CoffeeBean, Roaster, BrewMethod, Equipment constructible/mutable, and define write-repository contracts</name>
  <files>
    CoffeePeek.Shops.Domain/BussinessConstants.cs,
    CoffeePeek.Shops.Domain/Aggregates/CoffeeShopAggregate/Entities/City.cs,
    CoffeePeek.Shops.Domain/Aggregates/CoffeeShopAggregate/Entities/CoffeeBean.cs,
    CoffeePeek.Shops.Domain/Aggregates/CoffeeShopAggregate/Entities/Roaster.cs,
    CoffeePeek.Shops.Domain/Aggregates/BrewMethods/BrewMethod.cs,
    CoffeePeek.Shops.Domain/Aggregates/CoffeeShopAggregate/Entities/Equipment.cs,
    CoffeePeek.Shops.Domain/Aggregates/CoffeeShopAggregate/Repositories/ICityRepository.cs,
    CoffeePeek.Shops.Domain/Aggregates/CoffeeShopAggregate/Repositories/ICoffeeBeanRepository.cs,
    CoffeePeek.Shops.Domain/Aggregates/CoffeeShopAggregate/Repositories/IRoasterRepository.cs,
    CoffeePeek.Shops.Domain/Aggregates/CoffeeShopAggregate/Repositories/IEquipmentRepository.cs,
    CoffeePeek.Shops.Domain/Aggregates/CoffeeShopAggregate/Repositories/IBrewMethodRepository.cs,
    CoffeePeek.Shops.Domain.Tests/Entities/CoffeeShopAggregate/CityTests.cs,
    CoffeePeek.Shops.Domain.Tests/Entities/CoffeeShopAggregate/CoffeeBeanTests.cs,
    CoffeePeek.Shops.Domain.Tests/Entities/CoffeeShopAggregate/RoasterTests.cs,
    CoffeePeek.Shops.Domain.Tests/Entities/CoffeeShopAggregate/EquipmentTest.cs,
    CoffeePeek.Shops.Domain.Tests/Aggregates/BrewMethods/BrewMethodTests.cs
  </files>
  <behavior>
    - `new City("Minsk")` sets `Id` (non-empty Guid) and `Name`; blank/whitespace name throws `DomainException`; name longer than `BusinessConstants.MaxCityNameLength` (50) throws `DomainException`. `city.Update("Renamed")` changes `Name` with the same validation; `Update` with blank/too-long throws `DomainException` and leaves `Id` unchanged.
    - `new CoffeeBean("Arabica")` sets `Id` + `Name` with the same blank/too-long validation against `BusinessConstants.MaxCoffeeBeanNameLength` (100); `bean.Update("Robusta")` mutates `Name` with the same rules.
    - `new Roaster("Coffee Circus")` — identical shape to CoffeeBean, validated against `BusinessConstants.MaxRoasterNameLength` (100).
    - `new BrewMethod("V60", BrewMethodCategory.Gravity)` sets `Id`, `Name`, `Category`; blank/too-long name (`BusinessConstants.MaxBrewMethodNameLength` = 100) throws `DomainException`. `brewMethod.Update("Chemex", BrewMethodCategory.Gravity)` mutates both `Name` and `Category`.
    - `equipment.Update("Hario", "V60-02", category)` (new method on the existing `Equipment` class) mutates `Brand`, `ModelName`, `Category`, `CategoryId`, and recomputes `Name` as `$"{Brand} {ModelName}"` — mirrors the existing constructor's own validation exactly (`ArgumentException` with message "Brand is required" / "Model name is required" for blank brand/model — do NOT switch this one method to `DomainException`; stay consistent with the rest of the same file).
  </behavior>
  <action>
    In `BussinessConstants.cs`, add a new `#region Catalogs` block (next to the existing `#region ShopTag`) with `public const int MaxCityNameLength = 50;`, `MaxCoffeeBeanNameLength = 100`, `MaxRoasterNameLength = 100`, `MaxBrewMethodNameLength = 100`. Do not add length constants for Equipment brand/model — its existing constructor has no length check and `Update` should stay symmetric with it.

    In `City.cs`: change the `[MaxLength(50)]` attribute on `Name` to `[MaxLength(BusinessConstants.MaxCityNameLength)]` (same effective value, no EF model/migration change). Add a private static `ValidateName(string name)` that throws `CoffeePeek.Shared.Kernel.Exceptions.DomainException` ("Name is required." / "Name cannot be longer than {N} characters.") for blank or over-length input, mirroring `ShopTag.ValidateName`'s exact style. Call it from the existing constructor (trim and assign `Name` after validating) and from a new `public void Update(string name)` method. Add `using CoffeePeek.Shared.Kernel.Exceptions;` (note: `BusinessConstants` resolves without a `using` — `CoffeePeek.Shops.Domain` is an ancestor namespace of `CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate`, confirmed by `ShopTag.cs` doing the same).

    In `CoffeeBean.cs`: add a private parameterless constructor (`// ReSharper disable once UnusedMember.Local` + `private CoffeeBean() { }`) for EF materialization, a `public CoffeeBean(string name)` that validates+trims+assigns `Name` and sets `Id = Guid.NewGuid()`, and a `public void Update(string name)` reusing the same validation helper. Add `using CoffeePeek.Shared.Kernel.Exceptions;`.

    In `Roaster.cs`: identical shape to CoffeeBean.cs (private parameterless ctor, public `Roaster(string name)`, `Update(string name)`, `DomainException` validation against `MaxRoasterNameLength`).

    In `BrewMethod.cs`: add `using CoffeePeek.Shared.Kernel.Exceptions;`. Add a private parameterless constructor (BrewMethod currently has NO declared constructor at all, so adding any explicit constructor removes the implicit public parameterless one — EF needs the private one back). Add `public BrewMethod(string name, BrewMethodCategory category)` (sets `Id = Guid.NewGuid()`, validates+trims `Name`, assigns `Category`) and `public void Update(string name, BrewMethodCategory category)`.

    In `Equipment.cs`: add `public void Update(string brand, string modelName, EquipmentCategory category)` right after the existing constructor — same blank-check `ArgumentException`s as the constructor, then assign `Brand`, `ModelName`, `Category`, `CategoryId = category.Id`, and recompute `Name = $"{Brand} {ModelName}"`.

    Create the five new repository interfaces in `CoffeeShopAggregate/Repositories/` (same physical folder AND namespace, `CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate`, as the existing `IQueryCityRepository`/`IQueryEquipmentRepository`/`IQueryRoasterRepository`/`IQueryBrewMethodRepository` — note `IQueryBrewMethodRepository` already lives in this namespace even though `BrewMethod` itself is declared in `Aggregates.BrewMethods`; `IBrewMethodRepository` must mirror that same placement, with `using CoffeePeek.Shops.Domain.Aggregates.BrewMethods;` for the `BrewMethod` type):
    - `ICityRepository`: `Task<City?> GetByIdAsync(Guid id, CancellationToken ct = default)`, `void Add(City city)`, `void Remove(City city)`. (No `GetByNameAsync` — reuse the existing `IQueryCityRepository.GetByName` for duplicate checks in the handler.)
    - `ICoffeeBeanRepository`: `GetByIdAsync`, `Task<CoffeeBean?> GetByNameAsync(string name, CancellationToken ct = default)`, `Add`, `Remove`.
    - `IRoasterRepository`: same shape as `ICoffeeBeanRepository` for `Roaster`.
    - `IBrewMethodRepository`: same shape for `BrewMethod`.
    - `IEquipmentRepository`: `Task<Equipment?> GetByIdAsync(...)`, `Task<Equipment?> GetByBrandAndModelAsync(string brand, string modelName, CancellationToken ct = default)` (duplicate check, mirrors the equality check already used in `CoffeeShop.AddEquipment`), `Task<EquipmentCategory?> GetCategoryByIdAsync(int categoryId, CancellationToken ct = default)` (resolves the enum sent by the client to an actual `EquipmentCategory` row — `EquipmentCategories` is a small fixed lookup table matching `EquipmentCategoryEnum`, out of scope to CRUD), `Add`, `Remove`.

    Write domain tests mirroring `CoffeePeek.Shops.Domain.Tests/Aggregates/ShopTagAggregate/ShopTagTests.cs`'s style (plain xUnit `[Fact]`/`[Theory]` + FluentAssertions, no mocks): `CityTests.cs`, `CoffeeBeanTests.cs`, `RoasterTests.cs` (new files under `Entities/CoffeeShopAggregate/`, matching where the production types live), and `BrewMethodTests.cs` (new file, new folder `Aggregates/BrewMethods/`, matching where `BrewMethod.cs` lives). Each covers: valid construction sets `Id`/`Name` (+`Category` for BrewMethod); blank name throws `DomainException`; over-length name throws `DomainException`; `Update` mutates fields and re-validates. Append two new `[Fact]` methods to the EXISTING `CoffeePeek.Shops.Domain.Tests/Entities/CoffeeShopAggregate/EquipmentTest.cs` (do not create a new file) for the new `Update` method: one asserting a successful update mutates `Brand`/`ModelName`/`Category`/`Name`, one asserting blank brand throws `ArgumentException` — follow the file's existing `_testCategory = new EquipmentCategory()` fixture field and Arrange/Act/Assert comment style exactly.
  </action>
  <verify>
    <automated>dotnet build CoffeePeek.Shops.Domain/CoffeePeek.Shops.Domain.csproj && dotnet test CoffeePeek.Shops.Domain.Tests/CoffeePeek.Shops.Domain.Tests.csproj</automated>
  </verify>
  <done>Solution builds; all new and existing domain tests pass; no `[MaxLength]`/EF-model-affecting attribute was added to CoffeeBean, Roaster, BrewMethod, or Equipment (only City's existing attribute was repointed to a constant with the same value).</done>
</task>

<task type="auto">
  <name>Task 2: Persistence — implement the five write repositories and register them in DI</name>
  <files>
    CoffeePeek.Shops.Persistance/Repositories/QueryCityRepository.cs,
    CoffeePeek.Shops.Persistance/Repositories/QueryCoffeeBeanRepository.cs,
    CoffeePeek.Shops.Persistance/Repositories/QueryRoasterRepository.cs,
    CoffeePeek.Shops.Persistance/Repositories/QueryEquipmentRepository.cs,
    CoffeePeek.Shops.Persistance/Repositories/QueryBrewMethodRepository.cs,
    CoffeePeek.Shops.Persistance/DependencyInjection.cs
  </files>
  <action>
    Add one new class to the bottom of each existing file (mirroring `ShopTagRepository.cs`, which already holds both `ShopTagRepository` (write) and `QueryShopTagRepository` (read) side by side — do not create separate new files):
    - `QueryCityRepository.cs`: add `public class CityRepository(ShopsDbContext dbContext) : ICityRepository` — `GetByIdAsync` via `dbContext.Cities.FirstOrDefaultAsync(c => c.Id == id, ct)` (tracked, no `AsNoTracking` — this is the write side), `Add` via `dbContext.Cities.Add(city)`, `Remove` via `dbContext.Cities.Remove(city)`.
    - `QueryCoffeeBeanRepository.cs`: add `CoffeeBeanRepository : ICoffeeBeanRepository` — `GetByIdAsync`, `GetByNameAsync` via `dbContext.CoffeeBeans.FirstOrDefaultAsync(b => EF.Functions.ILike(b.Name, name), ct)` (case-insensitive, matches `IQueryCityRepository.GetByName`'s existing `ILike` pattern), `Add`, `Remove`.
    - `QueryRoasterRepository.cs`: add `RoasterRepository : IRoasterRepository` — same shape for `Roaster`/`dbContext.Roasters`.
    - `QueryBrewMethodRepository.cs`: add `BrewMethodRepository : IBrewMethodRepository` — same shape for `BrewMethod`/`dbContext.BrewMethods`.
    - `QueryEquipmentRepository.cs`: add `EquipmentRepository : IEquipmentRepository` — `GetByIdAsync` via `dbContext.Equipments`, `GetByBrandAndModelAsync` via `dbContext.Equipments.FirstOrDefaultAsync(e => e.Brand == brand && e.ModelName == modelName, ct)` (plain equality — matches the existing check in `CoffeeShop.AddEquipment`, not `ILike`), `GetCategoryByIdAsync` via `dbContext.EquipmentCategories.FirstOrDefaultAsync(c => c.Id == categoryId, ct)`, `Add`, `Remove`.

    In `CoffeePeek.Shops.Persistance/DependencyInjection.cs`, register the five new pairs next to the existing `services.AddScoped<IShopTagRepository, ShopTagRepository>();` line: `services.AddScoped<ICityRepository, CityRepository>();`, `ICoffeeBeanRepository/CoffeeBeanRepository`, `IRoasterRepository/RoasterRepository`, `IBrewMethodRepository/BrewMethodRepository`, `IEquipmentRepository/EquipmentRepository`. No new `using` directives are needed — the file already has `using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;` (covers all five new interfaces, including `IBrewMethodRepository` which lives in that same namespace) and `using CoffeePeek.Shops.Persistance.Repositories;` (covers all five new implementation classes).
  </action>
  <verify>
    <automated>dotnet build CoffeePeek.Shops.Persistance/CoffeePeek.Shops.Persistance.csproj</automated>
  </verify>
  <done>Solution builds; five new repository classes exist and are registered in DI; no repository-level unit tests are expected (this codebase has none for existing Query/Write repositories either — coverage comes from the Application-layer handler tests in Tasks 3–4).</done>
</task>

<task type="auto" tdd="true">
  <name>Task 3: Application — Create/Update/Delete for Cities, Beans, Roasters (the three Name-only catalog types)</name>
  <files>
    CoffeePeek.Shops.Application/Features/Admin/Catalogs/Cities/AdminCityHandlers.cs,
    CoffeePeek.Shops.Application/Features/Admin/Catalogs/Beans/AdminCoffeeBeanHandlers.cs,
    CoffeePeek.Shops.Application/Features/Admin/Catalogs/Roasters/AdminRoasterHandlers.cs,
    CoffeePeek.Shops.Application.Tests/Features/Admin/Catalogs/Cities/AdminCityHandlersTests.cs,
    CoffeePeek.Shops.Application.Tests/Features/Admin/Catalogs/Beans/AdminCoffeeBeanHandlersTests.cs,
    CoffeePeek.Shops.Application.Tests/Features/Admin/Catalogs/Roasters/AdminRoasterHandlersTests.cs
  </files>
  <behavior>
    For each of the three entities (City→`CityDto`, CoffeeBean→`CoffeeBeansDto`, Roaster→`RoasterDto`):
    - Create with a new name → `Response<TDto>.Success`, repository `Add` called once, `IUnitOfWork.SaveChangesAsync` called once, `ICacheService.RemoveByPattern` called with the entity's `ListPattern()` and with `CacheKey.Shop.SearchPattern()`.
    - Create with a name that already exists (duplicate lookup returns non-null) → `Response<TDto>.Error` with `StatusCode == 409`, `Add` never called.
    - Update on an existing id → mutates and returns `Response<TDto>.Success` with the new name; on a missing id → `Response<TDto>.Error` with `StatusCode == 404`.
    - Delete on an existing id → `Response.Success`, repository `Remove` called once; on a missing id → `Response.Error` with `StatusCode == 404`.
  </behavior>
  <action>
    Create `AdminCityHandlers.cs` (namespace `CoffeePeek.Shops.Application.Features.Admin.Catalogs.Cities`) with three commands + static handlers, following `AdminShopTagHandlers.cs`'s exact shape:
    - `public record CreateCityCommand(string Name);` / `CreateCityHandler.Handle(command, IQueryCityRepository queryRepository, ICityRepository repository, IMapper mapper, IUnitOfWork unitOfWork, ICacheService cacheService, CancellationToken ct)` — duplicate check via `queryRepository.GetByName(command.Name, ct)` (reuse the existing Query repo method — do not add a duplicate `GetByNameAsync` to `ICityRepository`), then `new City(command.Name)`, `repository.Add`, save, invalidate caches via a shared `internal static async Task InvalidateCityCachesAsync(ICacheService cacheService, CancellationToken ct)` helper (removes `CacheKey.City.ListPattern()` and `CacheKey.Shop.SearchPattern()`, mirroring `CreateShopTagHandler.InvalidateTagCachesAsync`), return `Response<CityDto>.Success(mapper.Map<CityDto>(city))`.
    - `public record UpdateCityCommand([property: JsonIgnore] Guid Id, string Name);` / `UpdateCityHandler` — `repository.GetByIdAsync`, 404 if null, `city.Update(command.Name)`, save, invalidate via `CreateCityHandler.InvalidateCityCachesAsync`, return success DTO.
    - `public record DeleteCityCommand([property: JsonIgnore] Guid Id);` / `DeleteCityHandler` returning `Response` (non-generic) — `repository.GetByIdAsync`, 404 if null, `repository.Remove`, save, invalidate, `Response.Success(message: "City deleted.")`.
    Let `City`/`CoffeeBean`/`Roaster` constructor `DomainException`s propagate uncaught (they map to 400 via `GlobalExceptionHandler`) — do not wrap in try/catch, matching `CreateShopTagHandler`.

    Create `AdminCoffeeBeanHandlers.cs` (namespace `...Catalogs.Beans`) and `AdminRoasterHandlers.cs` (namespace `...Catalogs.Roasters`) with the identical three-command shape, substituting `CoffeeBean`/`ICoffeeBeanRepository`/`CoffeeBeansDto`/`CacheKey.CoffeeBean` and `Roaster`/`IRoasterRepository`/`RoasterDto`/`CacheKey.Roaster` respectively. For these two, the duplicate check uses the NEW `GetByNameAsync` method added to `ICoffeeBeanRepository`/`IRoasterRepository` in Task 1 (there is no pre-existing Query-side `GetByName` for these two, unlike City) — inject only the single write repository, not a separate Query repository.

    Write handler tests mirroring `CreateShopTagHandlerTests`/`SetShopTagsHandlerTests` in `ShopTagHandlersTests.cs` (xUnit v3 `[Fact]` + Moq `Mock<T>` + FluentAssertions, one test class per Create/Update/Delete handler or grouped per entity file): mock the repository/query-repository, `IUnitOfWork`, `ICacheService`; assert on `result.IsSuccess`, `result.StatusCode`, `result.Data`, and `Verify(...)` calls exactly as `CreateShopTagHandlerTests.Handle_CreatesTagAndInvalidatesCache`/`Handle_DuplicateSlug_ReturnsConflict` do. One test file per entity (`AdminCityHandlersTests.cs`, `AdminCoffeeBeanHandlersTests.cs`, `AdminRoasterHandlersTests.cs`), each with Create/Update/Delete coverage per the `<behavior>` block above (minimum 2 tests per handler: success path + failure path).
  </action>
  <verify>
    <automated>dotnet test CoffeePeek.Shops.Application.Tests/CoffeePeek.Shops.Application.Tests.csproj --filter "FullyQualifiedName~Catalogs.Cities|FullyQualifiedName~Catalogs.Beans|FullyQualifiedName~Catalogs.Roasters"</automated>
  </verify>
  <done>All new Create/Update/Delete handlers for City, CoffeeBean, Roaster exist, compile, and pass their tests; each returns the existing public DTO (`CityDto`/`CoffeeBeansDto`/`RoasterDto`) — no new Admin-only DTO types were introduced for these three entities.</done>
</task>

<task type="auto" tdd="true">
  <name>Task 4: Application — Create/Update/Delete for BrewMethods and Equipment (the two category-aware catalog types)</name>
  <files>
    CoffeePeek.Contract/Enums/BrewMethodCategoryEnum.cs,
    CoffeePeek.Contract/Dtos/Shop/BrewMethodDto.cs,
    CoffeePeek.Shops.Application/Mapper/MapsterConfiguration.cs,
    CoffeePeek.Shops.Application/Features/Admin/Catalogs/BrewMethods/AdminBrewMethodHandlers.cs,
    CoffeePeek.Shops.Application/Features/Admin/Catalogs/Equipments/AdminEquipmentHandlers.cs,
    CoffeePeek.Shops.Application.Tests/Features/Admin/Catalogs/BrewMethods/AdminBrewMethodHandlersTests.cs,
    CoffeePeek.Shops.Application.Tests/Features/Admin/Catalogs/Equipments/AdminEquipmentHandlersTests.cs
  </files>
  <behavior>
    - BrewMethod: Create/Update accept a Contract-level `BrewMethodCategoryEnum` (not the Domain `BrewMethodCategory` enum directly — Application commands bound from controller request bodies should not force callers onto Domain types, matching how `EquipmentDto`/`EquipmentCategoryEnum` already keep the Domain `EquipmentCategory` entity out of the public surface). Success/duplicate/not-found behave exactly like Task 3's entities. `Response<BrewMethodDto>.Success(...)` now also returns the persisted `Category`.
    - Equipment: Create/Update accept `Brand`, `ModelName`, `EquipmentCategoryEnum Category`. If `GetCategoryByIdAsync((int)command.Category, ct)` returns null → `Response<EquipmentDto>.Error(HttpStatusCode.BadRequest, "Invalid equipment category.")`. Duplicate check is brand+model (case-sensitive equality, matching `CoffeeShop.AddEquipment`'s own check), not name.
  </behavior>
  <action>
    Create `CoffeePeek.Contract/Enums/BrewMethodCategoryEnum.cs` with values identical to the Domain `BrewMethodCategory` enum (`Unknown = 0, Pressure = 1, Gravity = 2, Immersion = 3, Traditional = 4`) — this is the Contract-side mirror, following the exact precedent of `EquipmentCategoryEnum` mirroring `EquipmentCategory`.

    Add `using CoffeePeek.Contract.Enums;` and a `public BrewMethodCategoryEnum Category { get; set; }` property to `BrewMethodDto.cs` (additive — existing consumers of this DTO, e.g. `ShortShopDto`/`ShopDto`/`CoffeeShopDetailsDto` embedding `BrewMethodDto`, are unaffected).

    In `MapsterConfiguration.cs`, add `using CoffeePeek.Shops.Domain.Aggregates.BrewMethods;` and a new `config.NewConfig<BrewMethod, BrewMethodDto>().Map(dest => dest.Category, src => (BrewMethodCategoryEnum)(int)src.Category);` entry (same simple-cast style as the existing `EquipmentCategory→EquipmentCategoryEnum` config a few lines above it). This also fixes the pre-existing gap where `GetAllBrewMethodsHandler`'s response silently dropped `Category` — that handler itself needs no code change, only the mapping config.

    Create `AdminBrewMethodHandlers.cs` (namespace `CoffeePeek.Shops.Application.Features.Admin.Catalogs.BrewMethods`) with the same three-command shape as Task 3, using `IBrewMethodRepository` (its own `GetByNameAsync` for the duplicate check), casting `(BrewMethodCategory)(int)command.Category` when calling `new BrewMethod(...)` / `brewMethod.Update(...)`, and invalidating `CacheKey.BrewMethod.ListPattern()` + `CacheKey.Shop.SearchPattern()`.

    Create `AdminEquipmentHandlers.cs` — namespace MUST be `CoffeePeek.Shops.Application.Features.Admin.Catalogs.Equipments` (plural — naming it `...Catalogs.Equipment` would collide with the `Equipment` domain type when unqualified `Equipment` is referenced inside that namespace block, since C# namespace member lookup walks up through enclosing namespaces). `CreateEquipmentCommand(string Brand, string ModelName, EquipmentCategoryEnum Category)` — resolve the category via `repository.GetCategoryByIdAsync((int)command.Category, ct)` (400 if null) before the duplicate check or entity construction; duplicate check via `repository.GetByBrandAndModelAsync(command.Brand, command.ModelName, ct)` (409 if found); construct via `new Equipment(command.Brand, command.ModelName, category)` (relying on the constructor's `isCustom = false, isPrimary = false` defaults — admin-catalog equipment is never "custom" or shop-scoped). `UpdateEquipmentCommand([property: JsonIgnore] Guid Id, string Brand, string ModelName, EquipmentCategoryEnum Category)` — 404 if missing, re-resolve category (400 if invalid), call `equipment.Update(brand, modelName, category)`. `DeleteEquipmentCommand([property: JsonIgnore] Guid Id)` returning `Response`. Invalidate `CacheKey.Equipment.ListPattern()` + `CacheKey.Shop.SearchPattern()`.

    Write `AdminBrewMethodHandlersTests.cs` and `AdminEquipmentHandlersTests.cs` following the same Moq/FluentAssertions pattern as Task 3's tests, additionally covering: invalid `EquipmentCategoryEnum` value → 400 (Equipment); category round-trips correctly through `Response<T>.Data` for both entities.
  </action>
  <verify>
    <automated>dotnet test CoffeePeek.Shops.Application.Tests/CoffeePeek.Shops.Application.Tests.csproj --filter "FullyQualifiedName~Catalogs.BrewMethods|FullyQualifiedName~Catalogs.Equipments"</automated>
  </verify>
  <done>`BrewMethodCategoryEnum` exists in Contract; `BrewMethodDto.Category` round-trips through the public GET endpoint and the new admin endpoints; Equipment Create/Update reject an unknown category with 400; both entities' handlers compile and pass their tests.</done>
</task>

<task type="auto">
  <name>Task 5: API — five new Admin controllers, widen AdminShopTagsController's policy, final solution-wide verification</name>
  <files>
    CoffeePeek.ShopsService/Controllers/AdminCitiesController.cs,
    CoffeePeek.ShopsService/Controllers/AdminBeansController.cs,
    CoffeePeek.ShopsService/Controllers/AdminRoastersController.cs,
    CoffeePeek.ShopsService/Controllers/AdminBrewMethodsController.cs,
    CoffeePeek.ShopsService/Controllers/AdminEquipmentsController.cs,
    CoffeePeek.ShopsService/Controllers/AdminShopTagsController.cs
  </files>
  <action>
    Create five controllers mirroring `AdminShopTagsController.cs` exactly (`[ApiController] [Route("api/admin/{segment}")] [Authorize(Policy = RoleConsts.Moderator)] [Tags("Admin")] [ProducesErrorResponseType(typeof(ErrorResponse))]`, primary constructor `(IMessageBus bus)`). Do NOT add a `[HttpGet]` list action to any of them — the public `CatalogsController` GET endpoints already cover listing for all five entities (unlike ShopTag, none of these five need admin-only fields in a list response), so only Create/Update/Delete are added:

    - `AdminCitiesController.cs`, route `api/admin/cities`: `[HttpPost] Create([FromBody] CreateCityCommand command, ct)` → `Response<CityDto>`, `response.StatusCode switch { StatusCodes.Status409Conflict => Conflict(response), _ => BadRequest(response) }` on failure; `[HttpPatch("{id:guid}")] Update(Guid id, [FromBody] UpdateCityRequest request, ct)` builds `new UpdateCityCommand(id, request.Name)`, returns `Ok`/`NotFound`; `[HttpDelete("{id:guid}")] Delete(Guid id, ct)` invokes `DeleteCityCommand(id)` returning `Response`, `Ok`/`NotFound`. Add `public record UpdateCityRequest(string Name);` at the bottom of the file (matching `UpdateShopTagRequest`'s placement in `AdminShopTagsController.cs`).
    - `AdminBeansController.cs`, route `api/admin/beans`: identical shape using `CreateCoffeeBeanCommand`/`UpdateCoffeeBeanCommand`/`DeleteCoffeeBeanCommand` from `AdminCoffeeBeanHandlers.cs`, `Response<CoffeeBeansDto>`, `UpdateCoffeeBeanRequest(string Name)`.
    - `AdminRoastersController.cs`, route `api/admin/roasters`: identical shape using the Roaster commands, `Response<RoasterDto>`, `UpdateRoasterRequest(string Name)`.
    - `AdminBrewMethodsController.cs`, route `api/admin/brew-methods`: uses the BrewMethod commands, `Response<BrewMethodDto>`; `UpdateBrewMethodRequest(string Name, BrewMethodCategoryEnum Category)`; controller builds `new UpdateBrewMethodCommand(id, request.Name, request.Category)`.
    - `AdminEquipmentsController.cs`, route `api/admin/equipments`: uses the Equipment commands, `Response<EquipmentDto>`; `UpdateEquipmentRequest(string Brand, string ModelName, EquipmentCategoryEnum Category)`; controller builds `new UpdateEquipmentCommand(id, request.Brand, request.ModelName, request.Category)`.

    In `AdminShopTagsController.cs`, change `[Authorize(Policy = RoleConsts.Admin)]` to `[Authorize(Policy = RoleConsts.Moderator)]` (the `Moderator` policy already grants `RoleConsts.Admin` too, per `RequireRole(RoleConsts.Moderator, RoleConsts.Admin)` in both `CoffeePeek.Gateway/Extensions/AuthExtensions.cs` and `CoffeePeek.ShopsService/DependencyInjection.cs` — confirmed identical in both places). Update the class's XML doc comment from "Admin CRUD for the global shop filter-tag catalog." to "Admin/Moderator CRUD for the global shop filter-tag catalog." No other change to this file.

    After all controllers are in place, run a full solution build and test pass to confirm every wave of this plan (domain, persistence, application, API) compiles and integrates correctly together.
  </action>
  <verify>
    <automated>dotnet build CoffeePeek.slnx && dotnet test CoffeePeek.slnx</automated>
  </verify>
  <done>Six admin routes now exist (`/api/admin/cities`, `/api/admin/beans`, `/api/admin/roasters`, `/api/admin/brew-methods`, `/api/admin/equipments`, `/api/admin/shop-tags`), all gated by `[Authorize(Policy = RoleConsts.Moderator)]`; the full solution builds and the full test suite passes.</done>
</task>

</tasks>

<threat_model>
## Trust Boundaries

| Boundary | Description |
|----------|--------------|
| Client → Gateway | Untrusted input; JWT validated, role claims extracted at the Gateway (`CoffeePeek.Gateway/Extensions/AuthExtensions.cs`) |
| Gateway → ShopsService | Gateway forwards trusted role/user headers; ShopsService re-validates the same `[Authorize(Policy = RoleConsts.Moderator)]` policy locally (defense in depth — `HeaderAuthenticationHandler` + `ShopsService/DependencyInjection.cs` policy registration, both pre-existing, unchanged by this plan) |
| Controller → Application handler | Command payload crosses from HTTP-bound DTO/route params into a Wolverine command; `Id` fields are `[JsonIgnore]` and always route-sourced, never body-sourced, to prevent a caller overriding the target entity id via the request body |

## STRIDE Threat Register

| Threat ID | Category | Component | Disposition | Mitigation Plan |
|-----------|----------|-----------|-------------|-----------------|
| T-quick260831-01 | Elevation of Privilege | Five new `Admin*Controller` classes + `AdminShopTagsController` | mitigate | `[Authorize(Policy = RoleConsts.Moderator)]` on every controller, enforced both at the Gateway (JWT role claim check) and locally in ShopsService (same policy re-registered) — a User/Owner/Employee/Roaster-role token is rejected with 403 |
| T-quick260831-02 | Tampering | `UpdateCityCommand`/`UpdateCoffeeBeanCommand`/etc. `Id` field | mitigate | `[property: JsonIgnore] Guid Id` — the command's `Id` is always constructed server-side from the route `{id:guid}` segment, never deserialized from the request body, so a malicious body cannot retarget the mutation to a different entity |
| T-quick260831-03 | Repudiation | Catalog Create/Update/Delete handlers | accept | No per-entry audit trail of which admin/moderator performed the change (only base `CreatedAtUtc`/`UpdatedAtUtc` via the existing `AuditInterceptor`) — matches the existing precedent (`AdminShopTagHandlers.cs` has the same gap); low-value target, six small reference tables, out of scope to fix here |
| T-quick260831-04 | Denial of Service | New POST/PATCH/DELETE admin endpoints | accept | Existing Gateway-level rate limiting (300 req/min global policy) applies; endpoints are role-gated to trusted internal Admin/Moderator users, not public-facing |
| T-quick260831-05 | Information Disclosure | `Response.Error`/`Response<T>.Error` messages returned to the client | accept | Messages are static, non-sensitive strings ("City not found.", "A city with this name already exists.") — no stack traces or internal details are included; unhandled exceptions still route through the existing `GlobalExceptionHandler`, which already redacts details in Release builds |
| T-quick260831-SC | Tampering (Supply Chain) | npm/NuGet installs | mitigate | No new NuGet packages are introduced by this plan — every type used (`Wolverine`, `Mapster`, `FluentAssertions`, `Moq`, `xunit`) is already a referenced dependency; no Package Legitimacy Gate is required |

</threat_model>

<verification>
- `dotnet build CoffeePeek.slnx` succeeds with no new warnings introduced by this plan's files.
- `dotnet test CoffeePeek.slnx` passes, including all new domain tests (Task 1) and Application handler tests (Tasks 3–4).
- Manual/API-level spot check (optional, not required for automated pass): with an Admin or Moderator JWT, `POST /api/admin/cities {"name":"Grodno"}` returns 200 with a new `CityDto`; `GET /api/catalogs/cities` (after the cache invalidation this plan adds) includes it; `DELETE /api/admin/cities/{id}` removes it; the same request with a User-role JWT returns 403.
- `git diff --stat` shows no changes to any `*.csproj`, `Directory.Packages.props`, or `Migrations/` file — confirms no new dependency and no schema change were introduced.
</verification>

<success_criteria>
- All six catalog types (cities, beans, equipments, roasters, brew-methods, shop-tags) have Create, Update, and Delete endpoints restricted to `RoleConsts.Moderator` (which includes Admin).
- Every new Create/Update handler reuses an existing Contract DTO (`CityDto`, `CoffeeBeansDto`, `RoasterDto`, `BrewMethodDto`, `EquipmentDto`) — no redundant Admin-only DTO was introduced where the public DTO already covers the shape (ShopTag's `AdminShopTagDto` was left untouched since it has admin-only fields the public `ShopTagDto` intentionally omits).
- No EF Core migration was created; `git status` shows no new files under `CoffeePeek.Shops.Persistance/Migrations/`.
- No public API contract in `CoffeePeek.Contract` was changed in a breaking way — the one addition (`BrewMethodDto.Category`) is purely additive.
- `dotnet build CoffeePeek.slnx` and `dotnet test CoffeePeek.slnx` both succeed.
</success_criteria>

<output>
Create `.planning/quick/260831-krk-add-full-crud-create-update-delete-for-c/260831-krk-SUMMARY.md` when done
</output>
