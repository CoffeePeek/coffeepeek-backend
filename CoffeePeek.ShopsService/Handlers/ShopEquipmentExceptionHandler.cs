using CoffeePeek.Shared.Kernel.ErrorCodes;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Persistance.Repositories;
using Microsoft.AspNetCore.Diagnostics;

namespace CoffeePeek.ShopsService.Handlers;

public sealed class ShopEquipmentExceptionHandler(ILogger<ShopEquipmentExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (!ShopEquipmentConstraintViolation.Matches(exception))
            return false;

        logger.LogWarning(exception, "Shop equipment conflict. TraceId: {TraceId}", httpContext.TraceIdentifier);
        httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
        await httpContext.Response.WriteAsJsonAsync(
            new ErrorResponse("Equipment primary selection or category conflicts with an existing shop assignment.",
                CommonErrorCodes.Conflict), cancellationToken);
        return true;
    }
}
