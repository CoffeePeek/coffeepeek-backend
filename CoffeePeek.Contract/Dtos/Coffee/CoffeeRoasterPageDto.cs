using System.Text.Json;
using System.Text.Json.Serialization;
using CoffeePeek.Contract.Dtos.PublicAddresses;

namespace CoffeePeek.Contract.Dtos.Coffee;

public sealed record CoffeeRoasterPageDto(PublicRoasterSummaryDto Roaster, PublicTagDto[] Tags,
    int AvailableProducts, DateTime? CatalogUpdatedAtUtc);
