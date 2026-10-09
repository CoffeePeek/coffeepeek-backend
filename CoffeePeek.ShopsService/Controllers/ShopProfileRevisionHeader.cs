using System.Globalization;

namespace CoffeePeek.ShopsService.Controllers;

public static class ShopProfileRevisionHeader
{
    public static bool TryParse(string? ifMatch, out long? expectedRevision)
    {
        expectedRevision = null;
        if (ifMatch is null)
            return true;

        var value = ifMatch.Trim();
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            value = value[1..^1];

        if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var revision))
            return false;

        expectedRevision = revision;
        return true;
    }
}
