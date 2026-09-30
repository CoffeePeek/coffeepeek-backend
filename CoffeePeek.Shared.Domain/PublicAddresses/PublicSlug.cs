using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace CoffeePeek.Shared.Domain.PublicAddresses;

/// <summary>Version-one public URL rules. Generated values are suggestions; the database reserves them.</summary>
public static partial class PublicSlug
{
    public const int MaxLength = 100;
    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
    {
        "api", "admin", "new", "edit", "create", "delete", "search", "me", "by-slug",
        "login", "logout", "register", "settings", "health", "swagger", "scalar", "null", "undefined",
        "public-addresses", "history", "backfill", "initialize", "rename", "batch", "visibility", "owner", "photos", "menu"
    };

    /// <summary>Manual input must already be canonical; never silently changes a requested URL.</summary>
    public static bool IsValid(string? value) => value is { Length: > 0 and <= MaxLength }
        && SlugPattern().IsMatch(value) && !Reserved.Contains(value) && !Guid.TryParse(value, out _);

    public static string Validate(string value)
    {
        if (!IsValid(value)) throw new ArgumentException("Invalid or reserved public slug.", nameof(value));
        return value;
    }

    public static string Generate(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var output = new StringBuilder();
        foreach (var character in name.Normalize(NormalizationForm.FormC).ToLowerInvariant())
        {
            var transliterated = character switch
            {
                'а' => "a", 'б' => "b", 'в' => "v", 'г' => "g", 'ґ' => "g", 'д' => "d",
                'е' => "e", 'ё' => "yo", 'ж' => "zh", 'з' => "z", 'и' => "i", 'і' => "i",
                'й' => "y", 'к' => "k", 'л' => "l", 'м' => "m", 'н' => "n", 'о' => "o",
                'п' => "p", 'р' => "r", 'с' => "s", 'т' => "t", 'у' => "u", 'ў' => "u",
                'ф' => "f", 'х' => "kh", 'ц' => "ts", 'ч' => "ch", 'ш' => "sh", 'щ' => "shch",
                'ъ' or 'ь' or '\'' or '’' => "", 'ы' => "y", 'э' => "e", 'ю' => "yu", 'я' => "ya",
                'ß' => "ss", 'æ' => "ae", 'œ' => "oe", 'ø' => "o", 'ł' => "l",
                _ => character.ToString()
            };
            foreach (var normalized in transliterated.Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(normalized) == UnicodeCategory.NonSpacingMark) continue;
                if (normalized is >= 'a' and <= 'z' or >= '0' and <= '9') output.Append(normalized);
                else if (output.Length > 0 && output[^1] != '-') output.Append('-');
            }
        }
        var candidate = output.ToString().Trim('-');
        if (candidate.Length > MaxLength) candidate = candidate[..MaxLength].TrimEnd('-');
        return IsValid(candidate) ? candidate : "place";
    }

    /// <summary>Deterministic candidates; retry only after a database uniqueness conflict.</summary>
    public static string Candidate(string name, Guid entityId, int attempt)
    {
        if (entityId == Guid.Empty) throw new ArgumentException("Owner is required.", nameof(entityId));
        ArgumentOutOfRangeException.ThrowIfNegative(attempt);
        var stem = Generate(name);
        if (attempt == 0) return stem;
        // Collision suffixes must not publish the aggregate's GUID, even partially.
        // Existing reserved addresses are unchanged; only new allocations use this digest.
        var identity = Convert.ToHexStringLower(SHA256.HashData(entityId.ToByteArray()))[..32];
        var suffix = attempt switch
        {
            1 => "-" + identity[..8],
            2 => "-" + identity[..12],
            3 => "-" + identity[..16],
            _ => "-" + identity + $"-{attempt.ToString(CultureInfo.InvariantCulture)}"
        };
        return stem[..Math.Min(stem.Length, MaxLength - suffix.Length)].TrimEnd('-') + suffix;
    }

    [GeneratedRegex("\\A[a-z0-9]+(?:-[a-z0-9]+)*\\z", RegexOptions.CultureInvariant)]
    private static partial Regex SlugPattern();
}
