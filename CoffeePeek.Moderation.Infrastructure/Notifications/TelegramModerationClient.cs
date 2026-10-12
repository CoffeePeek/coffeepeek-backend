using System.Net.Http.Json;
using System.Text.Json;
using CoffeePeek.Contract.Events.Moderation;
using Microsoft.Extensions.Options;

namespace CoffeePeek.Moderation.Infrastructure.Notifications;

public sealed class TelegramModerationClient(HttpClient httpClient, IOptions<TelegramModerationOptions> options)
{
    public async Task SendAsync(ModerationSubmissionCreated submission, CancellationToken ct)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.BotToken) || string.IsNullOrWhiteSpace(settings.ChatId))
            throw new InvalidOperationException("Telegram moderation bot token and chat ID must be configured.");

        // Telegram requires the token in the URL. This client has HTTP request logging disabled.
        using var response = await httpClient.PostAsJsonAsync($"/bot{settings.BotToken}/sendMessage",
            new { chat_id = settings.ChatId, text = Format(submission) }, ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Telegram sendMessage failed with HTTP {(int)response.StatusCode}.");

        using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        if (!body.RootElement.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True)
            throw new HttpRequestException("Telegram sendMessage returned an unsuccessful response.");
    }

    public static string Format(ModerationSubmissionCreated submission)
    {
        var kind = submission.Kind switch
        {
            ModerationSubmissionKind.Shop => "кофейня",
            ModerationSubmissionKind.Roaster => "обжарщик",
            ModerationSubmissionKind.ShopChange => "изменение кофейни",
            ModerationSubmissionKind.ShopIssue => "жалоба на кофейню",
            ModerationSubmissionKind.CheckIn => "чек-ин",
            ModerationSubmissionKind.CheckInReport => "жалоба на чек-ин",
            _ => throw new ArgumentOutOfRangeException(nameof(submission))
        };
        var lines = new List<string>
        {
            $"Новая заявка на модерацию: {kind}",
            $"ID заявки: {submission.SubmissionId}"
        };
        if (!string.IsNullOrWhiteSpace(submission.Subject))
            lines.Add($"Тема: {OneLine(submission.Subject, 200)}");
        if (submission.RelatedId is { } relatedId)
            lines.Add($"ID объекта: {relatedId}");
        if (submission.UserId is { } userId)
            lines.Add($"ID пользователя: {userId}");
        if (!string.IsNullOrWhiteSpace(submission.Details))
            lines.Add($"Детали: {OneLine(submission.Details, 500)}");
        return string.Join('\n', lines);
    }

    private static string OneLine(string value, int maxLength)
    {
        var line = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return line.Length <= maxLength ? line : line[..(maxLength - 1)] + "…";
    }
}
