using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace CoffeePeek.LegacyCheckInMigration;

public static class LegacyRow
{
    public static Guid Id(this JsonObject row, string key = "Id") => Guid.TryParse(row.Text(key), out var id) ? id : Guid.Empty;
    public static string? Text(this JsonObject row, string key) => row[key]?.GetValue<string>();
    public static int? Number(this JsonObject row, string key) => row[key]?.GetValue<int>();
    public static bool Flag(this JsonObject row, string key) => row[key]?.GetValue<bool>() == true;
    public static bool IsValidContent(JsonObject row, string text) =>
        row.Text(text)?.Trim().Length is >= 1 and <= 1000 &&
        row.Number("Rating_Place") is >= 1 and <= 5 && row.Number("Rating_Service") is >= 1 and <= 5 &&
        row.Number("Rating_Coffee") is >= 1 and <= 5;

    public static JsonObject Pick(this JsonObject row, params string[] fields)
    {
        var result = new JsonObject();
        foreach (var field in fields) result[field] = row[field]?.DeepClone();
        return result;
    }

    public static Guid StableId(string value) => new(SHA256.HashData(Encoding.UTF8.GetBytes("coffeepeek-legacy-check-ins-v1:" + value)).AsSpan(0, 16));

    public static bool SameContent(JsonObject left, string leftText, JsonObject right, string rightText) =>
        left.Id("UserId") == right.Id("UserId") && (left.Id("ShopId") == Guid.Empty ? left.Id("CoffeeShopId") : left.Id("ShopId")) == right.Id("ShopId") &&
        left.Text(leftText)?.Trim() == right.Text(rightText)?.Trim() &&
        new[] { "Rating_Place", "Rating_Service", "Rating_Coffee" }.All(key => left.Number(key) == right.Number(key)) &&
        new[] { "DrinkSlug", "CustomDrinkName", "DrinkNameRu", "DrinkNameEn" }.All(key => left.Text(key) == right.Text(key));
}
