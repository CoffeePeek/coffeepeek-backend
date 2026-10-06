using System.Text.Json;
using System.Text.Json.Serialization;
using CoffeePeek.Contract.Dtos.PublicAddresses;

namespace CoffeePeek.Contract.Dtos.Coffee;

public sealed record AdminCoffeeDto(Guid Id, Guid RoasterId, string? Slug, string Status,
    CoffeeContentDto Content, CountryDto[] Countries, string[] ProtectedFields,
    ShortPhotoMetadataDto[] Photos, CoffeeVariantDto[] Variants, string[] ReviewWarnings, long Version);
