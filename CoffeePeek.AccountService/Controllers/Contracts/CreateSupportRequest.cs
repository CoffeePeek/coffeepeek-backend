using System.ComponentModel.DataAnnotations;

namespace CoffeePeek.AccountService.Controllers.Contracts;

public sealed record CreateSupportRequest(
    [Required, EmailAddress, MaxLength(254)] string Email,
    [Required, MinLength(10), MaxLength(4000)] string Message);
