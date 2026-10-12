using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CoffeePeek.Moderation.Domain.Aggregates.ShopImportCandidateAggregate;
using CoffeePeek.Shared.Domain.Places;

using CoffeePeek.Moderation.Domain.Import;

namespace CoffeePeek.Moderation.Application.Features.Import.IngestImportFile;

public static class ImportFileParser
{
    public const int MaxPlaces = 5000;

    public static IReadOnlyList<ParsedImportPlace> Parse(JsonElement root)
    {
        var found = new List<ParsedImportPlace>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        Walk(root, found, seen, depth: 0);
        return found;
    }

    public static bool LooksLikeDecisionsFile(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty("decisions", out var decisions)
        && decisions.ValueKind == JsonValueKind.Object
        && Parse(root).Count == 0;

    private static void Walk(JsonElement node, List<ParsedImportPlace> found, HashSet<string> seen, int depth)
    {
        if (found.Count >= MaxPlaces || depth > 8)
            return;

        switch (node.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in node.EnumerateArray())
                    Walk(item, found, seen, depth + 1);
                return;
            case JsonValueKind.Object:
                if (TryReadPlace(node) is { } place && seen.Add(place.Snapshot.ExternalId))
                {
                    found.Add(place);
                    return;
                }

                foreach (var property in node.EnumerateObject())
                {
                    if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                        Walk(property.Value, found, seen, depth + 1);
                    if (found.Count >= MaxPlaces)
                        return;
                }

                return;
        }
    }

    private static ParsedImportPlace? TryReadPlace(JsonElement obj)
    {
        var props = Index(obj);
        if (props.TryGetValue("properties", out var nestedProps) && nestedProps.ValueKind == JsonValueKind.Object)
        {
            foreach (var pair in Index(nestedProps))
                props.TryAdd(pair.Key, pair.Value);
        }
        var tags = ReadStringMap(Get(props, "tags"));
        var name = FirstString(props, "name", "title", "Name", "displayName")
                   ?? NestedString(Get(props, "displayName"), "text")
                   ?? GetTag(tags, "name", "name:ru", "name:en");

        var (lat, lon) = ReadCoordinates(props);
        if (lat is null || lon is null)
            return null;

        var amenity = FirstString(props, "amenity") ?? GetTag(tags, "amenity");
        var shop = FirstString(props, "shop") ?? GetTag(tags, "shop");
        if (string.IsNullOrWhiteSpace(name) && amenity is null && shop is null)
            return null;

        var address = FirstString(props, "address", "address_name", "formattedAddress", "fullAddress")
                      ?? NestedString(Get(props, "CompanyMetaData"), "address")
                      ?? NestedString(Get(props, "properties"), "address")
                      ?? AddressFromTags(tags);

        var phone = FirstString(props, "phone", "nationalPhoneNumber", "internationalPhoneNumber", "phoneNumber")
                    ?? Read2GisContact(Get(props, "contact_groups"), "phone")
                    ?? ReadYandexPhone(Get(props, "CompanyMetaData"))
                    ?? GetTag(tags, "contact:phone", "phone");

        var website = FirstString(props, "website", "websiteUri", "url", "site")
                      ?? Read2GisContact(Get(props, "contact_groups"), "website")
                      ?? NestedString(Get(props, "CompanyMetaData"), "url")
                      ?? GetTag(tags, "website", "contact:website");

        var instagram = FirstString(props, "instagram")
                        ?? Read2GisContact(Get(props, "contact_groups"), "instagram")
                        ?? OsmCafeClassifier.InstagramUrl(tags, website);

        var openingHours = FirstString(props, "openingHours", "opening_hours", "hours")
                           ?? GetTag(tags, "opening_hours");
        var cuisine = FirstString(props, "cuisine") ?? GetTag(tags, "cuisine");
        var brand = FirstString(props, "brand") ?? GetTag(tags, "brand");
        var googleMapsUri = FirstString(props, "googleMapsUri", "googleMaps", "mapsUri")
                            ?? NestedString(Get(props, "links"), "googleMaps");

        var osmId = ReadOsmExternalId(props, tags);
        var source = osmId is not null ? ImportSource.Osm : ImportSource.File;
        var externalId = osmId
                         ?? FirstString(props, "externalId", "id", "placeId")
                         ?? FileExternalId(name, lat.Value, lon.Value);

        if (externalId.Length > 64)
            externalId = FileExternalId(name, lat.Value, lon.Value);

        if (tags.Count == 0)
        {
            if (!string.IsNullOrWhiteSpace(name))
                tags["name"] = name;
            tags["amenity"] = amenity ?? "cafe";
            if (!string.IsNullOrWhiteSpace(shop))
                tags["shop"] = shop;
            if (!string.IsNullOrWhiteSpace(cuisine))
                tags["cuisine"] = cuisine;
            else if (!string.IsNullOrWhiteSpace(name)
                     && (name.Contains("кофе", StringComparison.OrdinalIgnoreCase)
                         || name.Contains("coffee", StringComparison.OrdinalIgnoreCase)))
                tags["cuisine"] = "coffee_shop";
        }

        var snapshot = new OsmCandidateSnapshot(
            externalId,
            name,
            address,
            lat.Value,
            lon.Value,
            phone,
            website,
            instagram,
            openingHours,
            cuisine,
            brand,
            ReadTimestamp(props),
            FirstString(props, "checkDate", "check_date") ?? GetTag(tags, "check_date"),
            tags);

        return new ParsedImportPlace(snapshot, source, googleMapsUri);
    }

    private static Dictionary<string, JsonElement> Index(JsonElement obj)
    {
        var map = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in obj.EnumerateObject())
            map.TryAdd(property.Name, property.Value);
        return map;
    }

    private static JsonElement Get(Dictionary<string, JsonElement> props, string key) =>
        props.TryGetValue(key, out var value) ? value : default;

    private static string? FirstString(Dictionary<string, JsonElement> props, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!props.TryGetValue(key, out var value))
                continue;
            var text = AsString(value);
            if (!string.IsNullOrWhiteSpace(text))
                return text.Trim();
        }

        return null;
    }

    private static string? NestedString(JsonElement obj, params string[] keys)
    {
        if (obj.ValueKind != JsonValueKind.Object)
            return null;
        var props = Index(obj);
        return FirstString(props, keys);
    }

    private static string? AsString(JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };

    private static (decimal? Lat, decimal? Lon) ReadCoordinates(Dictionary<string, JsonElement> props)
    {
        var lat = AsDecimal(Get(props, "lat"))
                  ?? AsDecimal(Get(props, "latitude"))
                  ?? AsDecimal(Get(props, "Lat"));
        var lon = AsDecimal(Get(props, "lon"))
                  ?? AsDecimal(Get(props, "lng"))
                  ?? AsDecimal(Get(props, "longitude"))
                  ?? AsDecimal(Get(props, "Lon"));

        if (lat is not null && lon is not null)
            return (lat, lon);

        var point = Get(props, "point");
        if (point.ValueKind == JsonValueKind.Object)
        {
            var pointProps = Index(point);
            lat = AsDecimal(Get(pointProps, "lat"));
            lon = AsDecimal(Get(pointProps, "lon")) ?? AsDecimal(Get(pointProps, "lng"));
            if (lat is not null && lon is not null)
                return (lat, lon);
        }

        var location = Get(props, "location");
        if (location.ValueKind == JsonValueKind.Object)
        {
            var locProps = Index(location);
            lat = AsDecimal(Get(locProps, "latitude")) ?? AsDecimal(Get(locProps, "lat"));
            lon = AsDecimal(Get(locProps, "longitude")) ?? AsDecimal(Get(locProps, "lon"))
                  ?? AsDecimal(Get(locProps, "lng"));
            if (lat is not null && lon is not null)
                return (lat, lon);
        }

        var geometry = Get(props, "geometry");
        if (geometry.ValueKind == JsonValueKind.Object)
        {
            var geometryProps = Index(geometry);
            if (geometryProps.TryGetValue("coordinates", out var coords) && coords.ValueKind == JsonValueKind.Array)
            {
                var arr = coords.EnumerateArray().ToArray();
                if (arr.Length >= 2)
                    return (AsDecimal(arr[1]), AsDecimal(arr[0]));
            }
        }

        var center = Get(props, "center");
        if (center.ValueKind == JsonValueKind.Object)
        {
            var centerProps = Index(center);
            return (
                AsDecimal(Get(centerProps, "lat")),
                AsDecimal(Get(centerProps, "lon")));
        }

        return (null, null);
    }

    private static decimal? AsDecimal(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
            return number;
        if (value.ValueKind == JsonValueKind.String
            && decimal.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        return null;
    }

    private static Dictionary<string, string> ReadStringMap(JsonElement value)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (value.ValueKind != JsonValueKind.Object)
            return map;

        foreach (var property in value.EnumerateObject())
        {
            var text = AsString(property.Value);
            if (!string.IsNullOrWhiteSpace(text))
                map[property.Name] = text.Trim();
        }

        return map;
    }

    private static string? GetTag(Dictionary<string, string> tags, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (tags.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    private static string? AddressFromTags(Dictionary<string, string> tags)
    {
        var parts = new[]
        {
            GetTag(tags, "addr:street"),
            GetTag(tags, "addr:housenumber"),
            GetTag(tags, "addr:city")
        }.Where(p => !string.IsNullOrWhiteSpace(p));
        var line = string.Join(", ", parts);
        return string.IsNullOrWhiteSpace(line) ? GetTag(tags, "addr:full") : line;
    }

    private static string? ReadOsmExternalId(Dictionary<string, JsonElement> props, Dictionary<string, string> tags)
    {
        foreach (var key in new[] { "externalId", "osm", "osmId" })
        {
            var raw = FirstString(props, key);
            if (IsOsmId(raw))
                return raw!.Trim();
        }

        var type = FirstString(props, "type", "osmType") ?? GetTag(tags, "osm_type");
        var id = FirstString(props, "id", "osm_id") ?? GetTag(tags, "osm_id", "id");
        if (type is "node" or "way" or "relation" && !string.IsNullOrWhiteSpace(id))
            return $"{type}/{id}";

        return null;
    }

    private static bool IsOsmId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        var parts = value.Split('/', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 && parts[0] is "node" or "way" or "relation" && parts[1].All(char.IsDigit);
    }

    private static DateTimeOffset? ReadTimestamp(Dictionary<string, JsonElement> props)
    {
        var raw = FirstString(props, "osmUpdatedAt", "timestamp", "updatedAt");
        if (raw is null)
            return null;
        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }

    private static string? Read2GisContact(JsonElement contactGroups, string type)
    {
        if (contactGroups.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var group in contactGroups.EnumerateArray())
        {
            if (group.ValueKind != JsonValueKind.Object || !group.TryGetProperty("contacts", out var contacts))
                continue;
            if (contacts.ValueKind != JsonValueKind.Array)
                continue;
            foreach (var contact in contacts.EnumerateArray())
            {
                if (contact.ValueKind != JsonValueKind.Object)
                    continue;
                var contactType = NestedString(contact, "type");
                if (!string.Equals(contactType, type, StringComparison.OrdinalIgnoreCase))
                    continue;
                var value = NestedString(contact, "value", "text", "url");
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
        }

        return null;
    }

    private static string? ReadYandexPhone(JsonElement companyMeta)
    {
        if (companyMeta.ValueKind != JsonValueKind.Object)
            return null;
        if (!companyMeta.TryGetProperty("Phones", out var phones) &&
            !companyMeta.TryGetProperty("phones", out phones))
            return null;
        if (phones.ValueKind != JsonValueKind.Array)
            return null;
        foreach (var phone in phones.EnumerateArray())
        {
            var value = NestedString(phone, "formatted", "number", "value");
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    private static string FileExternalId(string? name, decimal lat, decimal lon)
    {
        var key = $"{ShopPlaceMatcher.NormalizeName(name)}|{decimal.Round(lat, 4)}|{decimal.Round(lon, 4)}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16].ToLowerInvariant();
        return $"file:{hash}";
    }
}
