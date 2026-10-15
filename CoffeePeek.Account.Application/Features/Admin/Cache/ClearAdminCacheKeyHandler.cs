using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel.Response;

namespace CoffeePeek.Account.Application.Features.Admin.Cache;

public static class ClearAdminCacheKeyHandler
{
    public static async Task<Response> Handle(
        ClearAdminCacheKeyCommand command,
        ICacheService cacheService,
        CancellationToken ct)
    {
        if (!AdminCachePatternValidator.IsAllowedKey(command.Key))
            return Response.Error(400, "Key is not allowed.");

        await cacheService.RemoveAsync(new CacheKey(command.Key));
        return Response.Success(message: "Cache key removed.");
    }
}
