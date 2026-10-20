using System.ComponentModel.DataAnnotations;

namespace CoffeePeek.AccountService.Controllers.Contracts;

public sealed record SendPushNotificationRequest(
    Guid UserId,
    [Required, StringLength(120, MinimumLength = 1)] string Title,
    [Required, StringLength(500, MinimumLength = 1)] string Body);
