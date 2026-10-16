using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CoffeePeek.Contract.Events.Moderation;
using CoffeePeek.Moderation.Application.Abstractions;
using CoffeePeek.Moderation.Domain.Aggregates.ModerationReviewAggregate;
using Microsoft.Extensions.Options;
using OpenTelemetry;

namespace CoffeePeek.Moderation.Infrastructure.Notifications;

public sealed class TelegramModerationClient(HttpClient httpClient, IOptions<TelegramModerationOptions> options,
    IQueryModerationReviewRepository reviews, TelegramCheckInPhotoClient photos)
{
    public async Task SendAsync(ModerationSubmissionCreated submission, CancellationToken ct)
    {
        var settings = options.Value;
        var review = submission.Kind == ModerationSubmissionKind.CheckIn
            ? await reviews.GetById(submission.SubmissionId, ct)
                ?? throw new InvalidOperationException("Check-in moderation submission was not found.")
            : null;
        object? markup = null;
        if (review is { ModerationStatus: Domain.Common.Enums.ModerationStatus.Pending } &&
            settings.ModeratorUserId != Guid.Empty &&
            long.TryParse(settings.ChatId, out var chatId) && chatId > 0)
        {
            markup = new { inline_keyboard = new object[][]
            {
                [new { text = "✅ Одобрить", callback_data = $"a:{review.Id:N}" },
                    new { text = "❌ Отклонить", callback_data = $"r:{review.Id:N}" }],
                [new { text = "Открыть очередь", url = $"{settings.AdminBaseUrl.TrimEnd('/')}/check-ins?status=Pending" }]
            } };
        }
        var text = Format(submission, review, settings);
        var message = markup is null
            ? (object)new { chat_id = settings.ChatId, text }
            : new { chat_id = settings.ChatId, text, reply_markup = markup };
        using var response = await PostJsonAsync("sendMessage", message, ct);
        if (review is null) return;
        var position = 0;
        foreach (var photo in review.ReviewPhotos.OrderBy(p => p.SortIndex))
        {
            using var download = await photos.DownloadAsync(review.UserId, review.CheckInId,
                photo.CheckInPhotoId!.Value, ct);
            download.EnsureSuccessStatusCode();
            await using var stream = await download.Content.ReadAsStreamAsync(ct);
            await SendPhotoAsync(stream, photo.FileName, photo.ContentType, ++position, ct);
        }
    }

    public async Task SendPhotoAsync(Stream photo, string fileName, string contentType, int position, CancellationToken ct)
    {
        var method = contentType is "image/jpeg" or "image/png" ? "sendPhoto" : "sendDocument";
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(options.Value.ChatId), "chat_id");
        form.Add(new StringContent($"Фото чек-ина {position}"), "caption");
        using var file = new StreamContent(photo);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, method == "sendPhoto" ? "photo" : "document", fileName);
        using var response = await PostAsync(method, form, ct);
    }

    public Task<JsonDocument> GetUpdatesAsync(long offset, CancellationToken ct) =>
        PostJsonAsync("getUpdates", new { offset, timeout = 20, allowed_updates = new[] { "callback_query" } }, ct);

    public async Task AnswerCallbackAsync(string callbackId, string text, CancellationToken ct)
    {
        using var response = await PostJsonAsync("answerCallbackQuery", new { callback_query_id = callbackId, text }, ct);
    }

    public async Task RemoveButtonsAsync(long messageId, CancellationToken ct)
    {
        using var response = await PostJsonAsync("editMessageReplyMarkup", new
        {
            chat_id = options.Value.ChatId, message_id = messageId,
            reply_markup = new { inline_keyboard = Array.Empty<object>() }
        }, ct);
    }

    private async Task<JsonDocument> PostJsonAsync(string method, object body, CancellationToken ct)
    {
        using var content = JsonContent.Create(body);
        return await PostAsync(method, content, ct);
    }

    private async Task<JsonDocument> PostAsync(string method, HttpContent content, CancellationToken ct)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.BotToken) || string.IsNullOrWhiteSpace(settings.ChatId))
            throw new InvalidOperationException("Telegram moderation bot token and chat ID must be configured.");
        var useRelay = !string.IsNullOrWhiteSpace(settings.RelayUrl);
        using var request = new HttpRequestMessage(HttpMethod.Post,
            useRelay ? new Uri(new Uri(settings.RelayUrl), method).ToString() : $"/bot{settings.BotToken}/{method}")
        {
            Content = content
        };
        if (useRelay)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.BotToken);
        // Direct Telegram URLs contain the bot token; keep credentials out of outbound telemetry.
        using var instrumentation = SuppressInstrumentationScope.Begin();
        using var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var description = await ReadErrorDescriptionAsync(response, settings.BotToken, ct);
            throw new HttpRequestException($"Telegram {method} failed with HTTP {(int)response.StatusCode}{description}.",
                null, response.StatusCode);
        }
        var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        if (!body.RootElement.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True)
        {
            var description = ErrorDescription(body.RootElement, settings.BotToken);
            body.Dispose();
            throw new HttpRequestException($"Telegram {method} returned an unsuccessful response{description}.",
                null, response.StatusCode);
        }
        return body;
    }

    private static async Task<string> ReadErrorDescriptionAsync(HttpResponseMessage response, string botToken,
        CancellationToken ct)
    {
        try
        {
            using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct),
                cancellationToken: ct);
            return ErrorDescription(body.RootElement, botToken);
        }
        catch (JsonException)
        {
            // A relay can return an HTML error page. Keep its body, which may contain credentials, out of logs.
            return "";
        }
    }

    private static string ErrorDescription(JsonElement body, string botToken)
    {
        if (body.ValueKind != JsonValueKind.Object ||
            !body.TryGetProperty("description", out var description) ||
            description.ValueKind != JsonValueKind.String)
            return "";

        var value = description.GetString()!
            .Replace(botToken, "[redacted]", StringComparison.Ordinal);
        value = new string(value.Select(c => char.IsControl(c) ? ' ' : c).ToArray()).Trim();
        return value.Length == 0 ? "" : $": {value[..Math.Min(value.Length, 200)]}";
    }

    public static string Format(ModerationSubmissionCreated submission, ModerationReview? review = null,
        TelegramModerationOptions? settings = null)
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
            lines.Add($"{(review is null ? "Тема" : "Автор")}: {OneLine(submission.Subject, 200)}");
        if (submission.RelatedId is { } relatedId)
            lines.Add($"ID объекта: {relatedId}");
        if (submission.UserId is { } userId)
            lines.Add($"ID пользователя: {userId}");
        if (review is not null)
        {
            lines.Add($"Статус: {review.ModerationStatus switch
            {
                Domain.Common.Enums.ModerationStatus.Approved when review.ModeratedBy is null => "одобрен автоматически",
                Domain.Common.Enums.ModerationStatus.Approved => "одобрен",
                Domain.Common.Enums.ModerationStatus.Rejected => "отклонён",
                _ => "ожидает решения"
            }}");
            lines.Add($"Версия чек-ина: {review.ContentRevision}");
            lines.Add($"Текст: {(string.IsNullOrWhiteSpace(review.Comment) ? "нет" : review.Comment)}");
            lines.Add($"Фотографии: {review.ReviewPhotos.Count}");
            if (settings is not null)
            {
                lines.Add($"Заявка: {settings.ApiBaseUrl.TrimEnd('/')}/api/v1/moderation/check-ins/{review.Id}");
                lines.Add($"Профиль автора: {settings.ApiBaseUrl.TrimEnd('/')}/api/admin/users/{review.UserId}/profile");
                lines.Add($"Админка: {settings.AdminBaseUrl.TrimEnd('/')}/check-ins?status=Pending");
            }
        }
        else if (!string.IsNullOrWhiteSpace(submission.Details))
            lines.Add($"Детали: {OneLine(submission.Details, 500)}");
        return string.Join('\n', lines);
    }

    private static string OneLine(string value, int maxLength)
    {
        var line = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return line.Length <= maxLength ? line : line[..(maxLength - 1)] + "…";
    }
}
