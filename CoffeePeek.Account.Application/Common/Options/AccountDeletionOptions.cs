using System.ComponentModel.DataAnnotations;

namespace CoffeePeek.Account.Application.Common.Options;

public class AccountDeletionOptions
{
    [Required]
    [MinLength(32)]
    public string TokenPepper { get; init; } = string.Empty;

    [Range(1, 168)]
    public int TokenLifetimeHours { get; init; } = 24;

    [Range(1, 1440)]
    public int ResendCooldownMinutes { get; init; } = 5;

    [Range(1, 20)]
    public int MaxEmailsPerHour { get; init; } = 3;
}
