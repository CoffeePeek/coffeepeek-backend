using System.ComponentModel.DataAnnotations;

namespace CoffeePeek.Account.Infrastructure.Options;

public sealed class SupportOptions
{
    [Required, EmailAddress]
    public string Email { get; set; } = "info@coffeepeek.by";
}
