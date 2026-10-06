using CoffeePeek.Contract.Dtos.Discovery;
using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Shops.Application.Features.Discovery;

public sealed class DiscoveryException(string message, int statusCode = 400, string errorCode = "INVALID_FILTER",
    Dictionary<string, string[]>? errors = null) : BaseException(message, errorCode, statusCode)
{
    public Dictionary<string, string[]> Errors { get; } = errors ?? [];
}
