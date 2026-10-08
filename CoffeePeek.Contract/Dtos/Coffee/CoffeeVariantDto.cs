using System.Text.Json;
using System.Text.Json.Serialization;
using CoffeePeek.Contract.Dtos.PublicAddresses;

namespace CoffeePeek.Contract.Dtos.Coffee;

public sealed record CoffeeVariantDto(int? WeightGrams, decimal Price, string Currency, string? Grind,
    string? RoastPurpose, string Availability, string AvailabilityScope, string SellerName,
    string SourceUrl, DateTime CheckedAtUtc);
