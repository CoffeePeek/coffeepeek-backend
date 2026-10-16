using System.ComponentModel.DataAnnotations;

namespace CoffeePeek.AccountService.Controllers.Contracts;

public sealed class AppleCallbackRequest
{
    [Required, StringLength(64, MinimumLength = 64)]
    public string State { get; set; } = string.Empty;

    [MaxLength(4096)]
    public string? Code { get; set; }

    [MaxLength(256)]
    public string? Error { get; set; }

    public override string ToString() => "AppleCallbackRequest { State = [REDACTED], Code = [REDACTED], Error = [REDACTED] }";
}
