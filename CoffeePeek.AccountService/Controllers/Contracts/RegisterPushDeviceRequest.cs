using System.ComponentModel.DataAnnotations;

namespace CoffeePeek.AccountService.Controllers.Contracts;

public sealed record RegisterPushDeviceRequest([Required, StringLength(4096, MinimumLength = 1)] string Token);
