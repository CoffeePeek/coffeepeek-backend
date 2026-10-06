using System.Text.Json;
using System.Text.Json.Serialization;
using CoffeePeek.Contract.Dtos.PublicAddresses;

namespace CoffeePeek.Contract.Dtos.Coffee;

public sealed record CoffeeProductDto(PublicAddressDto Address, PublicRoasterSummaryDto Roaster,
    CoffeeContentDto Content, CountryDto[] Countries, ShortPhotoMetadataDto[] Photos,
    CoffeeVariantDto[] Variants, DateTime? UpdatedAtUtc);
