using System.Text.Json;
using CoffeePeek.Contract.Enums;
using CoffeePeek.Moderation.Application.Features.Review.ChangeStatusModerationReview;
using CoffeePeek.Moderation.Infrastructure.Notifications;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;
using Microsoft.Extensions.Options;
using Wolverine;

namespace CoffeePeek.ModerationService.Telegram;

public sealed class TelegramModerationPoller(
    IServiceScopeFactory scopes,
    IOptions<TelegramModerationOptions> options,
    ILogger<TelegramModerationPoller> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        // shortcut: inline decisions support the configured private chat; add a separate Telegram user ID for groups.
        if (settings.ModeratorUserId == Guid.Empty ||
            !long.TryParse(settings.ChatId, out var chatId) || chatId <= 0)
            return;

        long offset = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var telegram = scope.ServiceProvider.GetRequiredService<TelegramModerationClient>();
                var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
                using var updates = await telegram.GetUpdatesAsync(offset, stoppingToken);
                foreach (var update in updates.RootElement.GetProperty("result").EnumerateArray())
                {
                    await ProcessAsync(update, chatId, settings.ModeratorUserId, bus, telegram, stoppingToken);
                    offset = update.GetProperty("update_id").GetInt64() + 1;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Telegram moderation polling failed");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task ProcessAsync(JsonElement update, long chatId, Guid moderatorId, IMessageBus bus,
        TelegramModerationClient telegram, CancellationToken ct)
    {
        if (!update.TryGetProperty("callback_query", out var callback) ||
            !callback.TryGetProperty("id", out var callbackId) ||
            !callback.TryGetProperty("message", out var message) ||
            !message.TryGetProperty("message_id", out var messageId))
            return;

        var result = "Нет доступа";
        if (TryParseAction(callback, chatId, out var submissionId, out var decision))
        {
            try
            {
                var response = await bus.InvokeAsync<UpdateEntityResponse<ModerationStatus>>(
                    new ChangeStatusModerationReviewCommand(moderatorId, submissionId, decision, null,
                        decision == ModerationStatus.Rejected ? "Отклонено модератором Telegram" : null, true), ct);
                result = response.IsSuccess
                    ? decision == ModerationStatus.Approved ? "Одобрено" : "Отклонено"
                    : "Заявка уже рассмотрена";
                try
                {
                    await telegram.RemoveButtonsAsync(messageId.GetInt64(), ct);
                }
                catch (HttpRequestException ex)
                {
                    logger.LogWarning(ex, "Could not clear Telegram moderation buttons");
                }
            }
            catch (NotFoundException)
            {
                result = "Заявка не найдена";
            }
        }

        try
        {
            await telegram.AnswerCallbackAsync(callbackId.GetString()!, result, ct);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Could not acknowledge Telegram moderation button");
        }
    }

    public static bool TryParseAction(JsonElement callback, long chatId, out Guid submissionId,
        out ModerationStatus decision)
    {
        submissionId = default;
        decision = default;
        if (!callback.TryGetProperty("from", out var from) ||
            !from.TryGetProperty("id", out var senderId) ||
            !callback.TryGetProperty("message", out var message) ||
            !message.TryGetProperty("chat", out var chat) ||
            !chat.TryGetProperty("id", out var messageChatId) ||
            senderId.GetInt64() != chatId || messageChatId.GetInt64() != chatId ||
            !callback.TryGetProperty("data", out var dataElement))
            return false;
        var data = dataElement.GetString();
        if (data is not { Length: 34 } || data[1] != ':' ||
            data[0] is not ('a' or 'r') || !Guid.TryParseExact(data.AsSpan(2), "N", out submissionId))
            return false;
        decision = data[0] == 'a' ? ModerationStatus.Approved : ModerationStatus.Rejected;
        return true;
    }
}
