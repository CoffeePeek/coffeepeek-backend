using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel.Response;

namespace CoffeePeek.Account.Application.Features.Admin.Cache;

public static class ClearAdminCacheByPatternHandler
{
    public static async Task<Response<ClearAdminCacheResponse>> Handle(
        ClearAdminCacheByPatternCommand command,
        ICacheService cacheService,
        CancellationToken ct)
    {
        if (!AdminCachePatternValidator.IsAllowed(command.Pattern))
            return Response<ClearAdminCacheResponse>.Error("Pattern is not allowed. It must start with a literal 'prefix:' and cannot target revocation keys.");

        var clearedCount = await cacheService.RemoveByPattern(command.Pattern, ct);

        return Response<ClearAdminCacheResponse>.Success(new ClearAdminCacheResponse(clearedCount, command.Pattern));
    }
}
