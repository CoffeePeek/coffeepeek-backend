using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shops.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Shops.Infrastructure.Menu;

public sealed class OpenAiMenuVisionParser(
    IHttpClientFactory httpClientFactory,
    IOptions<OpenAiOptions> options,
    ILogger<OpenAiMenuVisionParser> logger) : IMenuVisionParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<MenuVisionParseResult> ParseAsync(
        IReadOnlyList<MenuVisionPhoto> photos,
        CancellationToken ct = default)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            logger.LogError("Menu parse skipped: OpenAI API key is not configured (model {Model})", settings.Model);
            return new MenuVisionParseResult(false, "OpenAI API key is not configured.", []);
        }

        if (photos.Count == 0)
        {
            logger.LogWarning("Menu parse skipped: no menu photos supplied");
            return new MenuVisionParseResult(false, "No menu photos supplied.", []);
        }

        var content = new List<object>
        {
            new { type = "text", text = Prompt }
        };

        foreach (var photo in photos.Take(4))
        {
            var mime = Mime(photo.ContentType);
            content.Add(new
            {
                type = "image_url",
                image_url = new
                {
                    url = $"data:{mime};base64,{Convert.ToBase64String(photo.Bytes)}",
                    detail = "high"
                }
            });
        }

        var payload = new
        {
            model = settings.Model,
            temperature = 0,
            messages = new[]
            {
                new { role = "user", content }
            },
            response_format = ResponseFormat
        };

        var baseUrl = string.IsNullOrWhiteSpace(settings.BaseUrl)
            ? "https://api.openai.com/v1"
            : settings.BaseUrl.TrimEnd('/');
        var url = $"{baseUrl}/chat/completions";

        try
        {
            using var client = httpClientFactory.CreateClient("openai");
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", settings.ApiKey);
            using var response = await client.PostAsJsonAsync(url, payload, ct);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                logger.LogError(
                    "OpenAI menu parse HTTP {StatusCode} for model {Model}: {Body}",
                    (int)response.StatusCode,
                    settings.Model,
                    Trim(body));
                return new MenuVisionParseResult(false, FormatHttpError((int)response.StatusCode, body), []);
            }

            var completion = await response.Content.ReadFromJsonAsync<OpenAiChatResponse>(JsonOptions, ct);
            var message = completion?.Choices?.FirstOrDefault()?.Message;
            if (!string.IsNullOrWhiteSpace(message?.Refusal))
            {
                logger.LogError(
                    "OpenAI menu parse refused for model {Model}: {Refusal}",
                    settings.Model,
                    Trim(message.Refusal));
                return new MenuVisionParseResult(false, $"OpenAI refused the menu photo: {Trim(message.Refusal)}", []);
            }

            var text = message?.Content;
            if (string.IsNullOrWhiteSpace(text))
            {
                logger.LogError("OpenAI menu parse returned an empty response for model {Model}", settings.Model);
                return new MenuVisionParseResult(false, "OpenAI returned an empty response.", []);
            }

            ParsedMenuJson? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<ParsedMenuJson>(text, JsonOptions);
            }
            catch (JsonException ex)
            {
                logger.LogError(ex, "OpenAI menu parse JSON was invalid for model {Model}", settings.Model);
                return new MenuVisionParseResult(false, $"OpenAI JSON was invalid: {ex.Message}", []);
            }

            var drinks = (parsed?.Drinks ?? [])
                .Where(d => !string.IsNullOrWhiteSpace(d.RawName))
                .Select(d => new VisionDrinkLine(d.RawName!.Trim(), d.Price, d.VolumeMl, d.Confidence))
                .ToArray();

            logger.LogInformation(
                "OpenAI menu parse succeeded for model {Model}: {DrinkCount} drinks from {PhotoCount} photos",
                settings.Model,
                drinks.Length,
                photos.Count);

            return new MenuVisionParseResult(true, null, drinks);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Polly.Timeout.TimeoutRejectedException ex)
        {
            logger.LogWarning(
                ex,
                "OpenAI menu parse timed out for model {Model} after {TimeoutSeconds}s",
                settings.Model,
                settings.TimeoutSeconds);
            return new MenuVisionParseResult(
                false,
                $"OpenAI request timed out after {settings.TimeoutSeconds}s.",
                []);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "OpenAI menu parse threw for model {Model}", settings.Model);
            return new MenuVisionParseResult(false, $"OpenAI request failed ({ex.GetType().Name}).", []);
        }
    }

    private static string Mime(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
            return "image/jpeg";

        var mime = contentType.Split(';', 2)[0].Trim();
        return string.IsNullOrWhiteSpace(mime) ? "image/jpeg" : mime;
    }

    private static string FormatHttpError(int statusCode, string body)
    {
        if (body.Contains("unsupported_country_region_territory", StringComparison.OrdinalIgnoreCase)
            || body.Contains("Country, region, or territory not supported", StringComparison.OrdinalIgnoreCase))
        {
            return "OpenAI is blocked for this server IP. Set OpenAiOptions:ProxyUrl to an HTTP or SOCKS proxy whose egress is in a supported region.";
        }

        // The raw body is logged only: ParseError is shown in the admin UI and copied into Moderation, and
        // OpenAI error bodies include a partially masked API key and proxy/host details.
        return $"OpenAI HTTP {statusCode}.";
    }

    private static string Trim(string value) =>
        value.Length <= 300 ? value : value[..300];

    private const string Prompt =
        """
        Extract coffee drinks from this cafe menu photo. Ignore food, desserts, merch.
        Return JSON only. Prices are Belarusian rubles (BYN) unless another currency is explicit.
        rawName is the drink name as printed. volumeMl if a size in ml is printed, else null.
        """;

    private static readonly object ResponseFormat = new
    {
        type = "json_schema",
        json_schema = new
        {
            name = "cafe_menu",
            strict = true,
            schema = new
            {
                type = "object",
                additionalProperties = false,
                properties = new
                {
                    drinks = new
                    {
                        type = "array",
                        items = new
                        {
                            type = "object",
                            additionalProperties = false,
                            properties = new
                            {
                                rawName = new { type = "string" },
                                price = new { type = new[] { "number", "null" } },
                                volumeMl = new { type = new[] { "integer", "null" } },
                                confidence = new { type = new[] { "number", "null" } }
                            },
                            required = new[] { "rawName", "price", "volumeMl", "confidence" }
                        }
                    }
                },
                required = new[] { "drinks" }
            }
        }
    };

    private sealed class ParsedMenuJson
    {
        public List<ParsedDrinkJson> Drinks { get; set; } = [];
    }

    private sealed class ParsedDrinkJson
    {
        public string? RawName { get; set; }
        public decimal? Price { get; set; }
        public int? VolumeMl { get; set; }
        public double? Confidence { get; set; }
    }

    private sealed class OpenAiChatResponse
    {
        public List<OpenAiChoice>? Choices { get; set; }
    }

    private sealed class OpenAiChoice
    {
        public OpenAiMessage? Message { get; set; }
    }

    private sealed class OpenAiMessage
    {
        public string? Content { get; set; }

        [JsonPropertyName("refusal")]
        public string? Refusal { get; set; }
    }
}
