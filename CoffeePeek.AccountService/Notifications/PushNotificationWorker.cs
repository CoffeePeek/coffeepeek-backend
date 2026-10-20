using CoffeePeek.Account.Application.Features.Notifications;
using CoffeePeek.Account.Infrastructure.Notifications;
using Microsoft.Extensions.Options;

namespace CoffeePeek.AccountService.Notifications;

public sealed class PushNotificationWorker(IServiceScopeFactory scopes, IOptions<PushNotificationOptions> options,
    ILogger<PushNotificationWorker> logger, TimeProvider time) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        var nextMaintenance = DateTime.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var store = scope.ServiceProvider.GetRequiredService<IPushNotificationStore>();
                if (time.GetUtcNow().UtcDateTime >= nextMaintenance)
                {
                    await store.PurgeAsync(stoppingToken);
                    await store.QueueRemindersAsync(options.Value.InactiveDays, options.Value.ReminderRepeatDays, stoppingToken);
                    nextMaintenance = time.GetUtcNow().UtcDateTime.AddMinutes(15);
                }
                var dispatch = await store.ClaimAsync(stoppingToken);
                if (dispatch is null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), time, stoppingToken);
                    continue;
                }
                PushSendResult result;
                try
                {
                    var sender = scope.ServiceProvider.GetRequiredService<IPushSender>();
                    result = await sender.SendAsync(dispatch, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                catch (Exception)
                {
                    // Credentials, HTTP responses and tokens must never enter logs or error trackers.
                    logger.LogWarning("Push delivery {DeliveryId} will be retried", dispatch.DeliveryId);
                    result = new("Retry", TimeSpan.FromMinutes(5));
                }
                await store.CompleteAsync(dispatch, result, stoppingToken);
                if (result.Outcome != "Accepted")
                    logger.LogWarning("Push delivery {DeliveryId}: {Outcome}", dispatch.DeliveryId, result.Outcome);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError("Push worker failed ({ErrorType}); retrying in one minute", ex.GetType().Name);
                await Task.Delay(TimeSpan.FromMinutes(1), time, stoppingToken);
            }
        }
    }
}
