namespace CoffeePeek.Shops.Application.Features.CoffeeShop;

/// <summary>
/// Keeps DTOs containing IsOpen cached only inside the UTC minute in which they were evaluated.
/// Schedule values have minute precision, so the flag cannot change before this TTL expires.
/// </summary>
public static class ShopScheduleCachePolicy
{
    public static TimeSpan UntilNextUtcMinute(DateTime utcNow)
    {
        if (utcNow.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Cache expiration must be calculated from a UTC DateTime.", nameof(utcNow));

        var nextMinute = new DateTime(
            utcNow.Year,
            utcNow.Month,
            utcNow.Day,
            utcNow.Hour,
            utcNow.Minute,
            0,
            DateTimeKind.Utc).AddMinutes(1);

        return nextMinute - utcNow;
    }
}
