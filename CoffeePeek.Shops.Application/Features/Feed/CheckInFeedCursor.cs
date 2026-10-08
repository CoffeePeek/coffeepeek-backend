using System.Globalization;
using System.Text;
using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Shops.Application.Features.Feed;

// Version and canonical scope prevent silently reusing a cursor with another feed/filter.
// A cursor is a position, not an authorization token; visibility is checked on every read.
public static class CheckInFeedCursor
{
    public static string Encode(CheckInFeedPosition position, CheckInFeedScope scope)
    {
        var value = string.Join('|', "1", position.PublishedAtUtc.Ticks.ToString(CultureInfo.InvariantCulture),
            position.CheckInId.ToString("D"), Key(scope.CityId), Key(scope.ShopId), Key(scope.AuthorId));
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static CheckInFeedPosition Decode(string cursor, CheckInFeedScope scope)
    {
        try
        {
            if (cursor.Length is 0 or > 512 || cursor.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
                throw new FormatException();
            var base64 = cursor.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight((base64.Length + 3) / 4 * 4, '=');
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(base64)).Split('|');
            if (parts.Length != 6 || parts[0] != "1"
                || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) || ticks <= 0
                || !Guid.TryParseExact(parts[2], "D", out var id) || id == Guid.Empty
                || parts[3] != Key(scope.CityId) || parts[4] != Key(scope.ShopId) || parts[5] != Key(scope.AuthorId))
                throw new FormatException();
            return new CheckInFeedPosition(new DateTime(ticks, DateTimeKind.Utc), id);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            throw new ValidationException("Invalid feed cursor or cursor does not match the feed filters.");
        }
    }

    private static string Key(Guid? id) => id?.ToString("D") ?? "-";
}
