using CoffeePeek.Account.Domain.Entities.Notifications;

namespace CoffeePeek.Account.Application.Features.Notifications;

public sealed record PushDispatch(Guid DeliveryId, Guid LeaseId, string Token, PushNotification Notification);
