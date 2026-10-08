using CoffeePeek.Shared.Kernel.ErrorCodes;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Persistance.Repositories;
using Microsoft.AspNetCore.Diagnostics;

namespace CoffeePeek.ShopsService.Handlers;

public sealed class CityReferenceExceptionHandler(ILogger<CityReferenceExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (!CityReferenceViolation.Matches(exception))
            return false;

        logger.LogWarning(exception, "City reference conflict. TraceId: {TraceId}", httpContext.TraceIdentifier);
        httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
        await httpContext.Response.WriteAsJsonAsync(
            new ErrorResponse("Referenced city does not exist or is still in use.", CommonErrorCodes.Conflict),
            cancellationToken);
        return true;
    }
}
