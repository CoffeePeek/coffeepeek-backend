using System.Globalization;
using System.Text.RegularExpressions;
using CoffeePeek.Contract.Dtos.Coffee;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate;

namespace CoffeePeek.Shops.Persistance.CoffeeCatalog;

/// <summary>Recognizes explicit source attributes only. Names, countries and grind options are never evidence.</summary>
public static class CoffeeClassificationNormalizer
{
    public static CoffeeClassificationData Normalize(CoffeeSnapshotProduct product)
    {
        var processing = Text(product.Processing);
        var features = product.Features.Select(Text).ToArray();
        var evidence = string.Join(";", features.Prepend(processing));
        var caffeine = features.FirstOrDefault(v => v is "regular" or "с кофеином") is not null ? "regular" : null;
        if (Has(evidence, "lowcaf", "лоукаф", "низкокофеин", "50% кофеина", "50 % кофеина")) caffeine = "lowcaf";
        else if (Has(evidence, "decaf", "декаф", "без кофеина", "декофеин", "swiss water")) caffeine = "decaf";

        var processingCodes = new List<string>();
        if (Has(processing, "washed", "мытый", "мытая", "мытой", "двойная промывка")) processingCodes.Add("washed");
        if (Has(processing, "natural", "натуральн", "сухой", "сухая")) processingCodes.Add("natural");
        if (Has(processing, "honey", "хани", "медовая")) processingCodes.Add("honey");
        if (Has(processing, "wet-hulled", "wet hulled", "вет-халл", "гиллинг басах")) processingCodes.Add("wet-hulled");

        var fermentation = new List<string>();
        if (Has(evidence, "semi-anaerobic", "semi anaerobic", "полуанаэроб")) fermentation.Add("semi-anaerobic");
        else if (Has(evidence, "anaerobic", "анаэроб")) fermentation.Add("anaerobic");
        if (Has(evidence, "experimental", "экспериментальн")) fermentation.Add("experimental");

        var brew = product.BrewRecommendations.Concat(product.Features).Select(NormalizeBrewPurpose)
            .OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
        var roast = Text(product.RoastLevel) switch
        {
            "light" or "светлая" or "светлый" or "легкая" or "легкий" or "светлая обжарка" or "светлая обжарка кофе" or "светлая обжарка, фильтр" => "light",
            "medium" or "медиум" or "средняя" or "средний" or "средняя обжарка" or "средняя обжарка кофе" or "средняя обжарка кофе, эспрессо" => "medium",
            "dark" or "темная" or "темный" or "темная обжарка" or "темная обжарка кофе" or "темная, эспрессо" or "эспрессо, темная" => "dark", _ => null
        };
        var acidity = Text(product.Acidity) switch
        {
            "low" or "низкая" or "низкий" or "ниже средней" => "low",
            "balanced" or "medium" or "средняя" or "умеренная" => "balanced",
            "bright" or "high" or "высокая" or "яркая" or "выше средней" => "bright", _ => null
        };
        // A denominator is explicit source evidence. Bare scores have no known source scale and remain unknown.
        if (acidity is null && product.Acidity is not null)
        {
            var score = Regex.Match(product.Acidity.Trim(), "\\A(?<score>[0-9]+(?:[.,][0-9]+)?)\\s*/\\s*(?<max>[0-9]+(?:[.,][0-9]+)?)\\z");
            if (score.Success && decimal.TryParse(score.Groups["score"].Value.Replace(',', '.'), NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out var value) && decimal.TryParse(score.Groups["max"].Value.Replace(',', '.'),
                    NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var maximum) && maximum > 0 && value <= maximum)
                acidity = value / maximum <= 0.4m ? "low" : value / maximum <= 0.6m ? "balanced" : "bright";
        }

        var taste = new List<string>();
        foreach (var descriptor in product.TasteDescriptors.Select(Text))
        {
            if (Has(descriptor, "chocolate", "cocoa", "шоколад", "какао")) taste.Add("chocolate");
            if (Has(descriptor, "nut", "nuts", "nutty", "almond", "hazelnut", "орех", "миндаль", "фундук", "арахис", "пекан")) taste.Add("nut");
            if (Has(descriptor, "caramel", "карамель", "ирис")) taste.Add("caramel");
            if (Has(descriptor, "berry", "berries", "ягод", "клубник", "малин", "смородин", "черник", "ежевик", "вишн")) taste.Add("berry");
            if (Has(descriptor, "citrus", "цитрус", "апельсин", "лимон", "лайм", "грейпфрут", "мандарин")) taste.Add("citrus");
            if (Has(descriptor, "tropical", "тропическ", "ананас", "манго", "маракуй", "папай", "банан", "личи")) taste.Add("tropical");
            if (Has(descriptor, "floral", "цветоч", "цветы", "жасмин", "бергамот", "лаванд")) taste.Add("floral");
            if (Has(descriptor, "spicy", "spices", "nutmeg", "прян", "специи", "кориц", "кардамон", "гвоздик", "мускат")) taste.Add("spicy");
        }
        var composition = product.CompositionKind switch { "single_origin" => "single-origin", "blend" => "blend", _ => null };
        if (composition is null && features.Any(v => v is "single-origin" or "single origin" or "моносорт")) composition = "single-origin";
        if (composition is null && features.Any(v => v is "blend" or "смесь" or "бленд")) composition = "blend";
        return new(brew, caffeine, roast, acidity, processingCodes.Distinct().ToArray(),
            fermentation.Distinct().ToArray(), taste.Distinct().ToArray(), composition);
    }

    public static string? NormalizeBrewPurpose(string? value) => Text(value) switch
    {
        "filter" or "фильтр" or "для фильтра" or "кофе для фильтра" or "обжарка под фильтр" or "под фильтр" => "filter",
        "espresso" or "эспрессо" or "для эспрессо" or "кофе для эспрессо" or "обжарка под эспрессо" or "под эспрессо" => "espresso",
        "omni" or "omniroast" or "омни" or "омнирост" or "универсальная обжарка" => "omni", _ => null
    };

    public static CoffeeClassificationData ActiveOnly(CoffeeClassificationData data, IReadOnlyCollection<CoffeeFilterValue> active)
    {
        bool Present(string group, string? code) => code is not null && active.Any(v => v.IsActive && v.GroupCode == group && v.Code == code);
        string[] Many(string group, string[] values) => values.Where(code => Present(group, code)).ToArray();
        string? One(string group, string? code) => Present(group, code) ? code : null;
        return new(Many("brew", data.DefaultBrewPurposes), One("caffeine", data.Caffeine), One("roast", data.RoastLevel),
            One("acidity", data.Acidity), Many("processing", data.Processing), Many("fermentation", data.Fermentation),
            Many("taste", data.TasteGroups), One("composition", data.Composition));
    }

    private static string Text(string? text) => (text ?? "").Trim().ToLowerInvariant().Replace('ё', 'е');
    private static bool Has(string text, params string[] evidence) => evidence.Any(token =>
        token.All(c => c is >= 'a' and <= 'z' || c is ' ' or '-')
            ? Regex.IsMatch(text, "(?<![a-z])" + Regex.Escape(token) + "(?![a-z])", RegexOptions.CultureInvariant)
            : text.Contains(token, StringComparison.Ordinal));
}
