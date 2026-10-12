namespace CoffeeShop.Moderation.Persistence.Repositories;

/// <summary>
/// Visible-field match for the import queue "Name" box: name and address only.
/// Brand, email, website, and OSM id are not searched — they produce false hits
/// (for example Coffee Embassy matching "lavazza" via an unrelated contact tag).
/// </summary>
public static class ImportCandidateTextSearch
{
    public static bool MatchesNameOrAddress(string? search, string? name, string? address)
    {
        if (string.IsNullOrWhiteSpace(search))
            return true;

        var term = search.Trim();
        return Contains(name, term) || Contains(address, term);
    }

    public static string ToILikeContainsPattern(string search)
    {
        var escaped = search.Trim()
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
        return "%" + escaped + "%";
    }

    private static bool Contains(string? haystack, string needle) =>
        haystack is not null && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
