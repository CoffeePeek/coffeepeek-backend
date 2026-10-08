using CoffeePeek.Contract.Dtos.Schedule;
using CoffeePeek.Contract.Enums;
using DomainCoffeeShopStatus = CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate.CoffeeShopStatus;
using ContractPriceRange = CoffeePeek.Contract.Enums.PriceRange;

namespace CoffeePeek.Shops.Application.Features.Admin.Shops;

/// <summary>Published coffee shop summary for admin and owner portals.</summary>
public record AdminPublishedShopDto(
    Guid Id,
    string Name,
    Guid CityId,
    DomainCoffeeShopStatus Status,
    CoffeeShopType? Type,
    Guid? CreatorId,
    Guid? OwnerUserId,
    Guid? ModerationId,
    DateTime CreatedAtUtc,
    bool IsHidden,
    DateTime? ImportedFromFileAt,
    IReadOnlyList<AdminShopPhotoDto> Photos,
    string? Description = null,
    ContractPriceRange? PriceRange = null,
    AdminShopLocationDto? Location = null,
    AdminShopContactsDto? Contacts = null,
    IReadOnlyList<ScheduleDto>? Schedules = null,
    IReadOnlyList<Guid>? EquipmentIds = null,
    IReadOnlyList<Guid>? BeanIds = null,
    IReadOnlyList<Guid>? RoasterIds = null,
    IReadOnlyList<Guid>? BrewMethodIds = null,
    int DataCompletenessScore = 0,
    IReadOnlyList<Guid>? TagIds = null,
    IReadOnlyList<Guid>? PrimaryEquipmentIds = null,
    long ProfileRevision = 0);
