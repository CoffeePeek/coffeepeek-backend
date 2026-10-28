using CoffeePeek.Contract.Dtos.Menu;
using CoffeePeek.Contract.Dtos.Schedule;
using CoffeePeek.Contract.Enums;

namespace CoffeePeek.Contract.Events.Moderation;

public record ImportCandidatePublishedItem(
    Guid CandidateId,
    Guid CreatorId,
    string Name,
    string Address,
    decimal Latitude,
    decimal Longitude,
    Guid CityId,
    string? Phone,
    string? Website,
    string? Instagram,
    CoffeeShopType Type,
    string[] TagSlugs,
    bool TemporarilyClosed,
    bool ImportedFromFile = false,
    ShopMenuSnapshot? Menu = null,
    IReadOnlyList<ScheduleDto>? Schedules = null);
