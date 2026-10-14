using CoffeePeek.Shared.Domain.PublicAddresses;

namespace CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;

/// <summary>Current catalog slugs plus the codes persisted by older mobile clients.</summary>
public static class HomeBrewMethodCode
{
    public static bool IsLegacy(string? code) => code is
        "espresso" or "pour_over" or "batch_drip" or "moka_pot" or "cezve" or "aeropress" or "french_press" or "other";

    public static bool IsValid(string code) => IsLegacy(code) || PublicSlug.IsValid(code);
}