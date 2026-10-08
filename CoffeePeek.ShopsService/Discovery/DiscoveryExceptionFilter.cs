using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.Discovery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CoffeePeek.ShopsService.Discovery;

/// <summary>Preserve legacy error behavior while giving the new contracts their documented codes.</summary>
public sealed class DiscoveryExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (!IsDiscoveryAction(context.ActionDescriptor) || context.Exception is not BaseException) return;
        var exception = (BaseException)context.Exception;
        var status = exception.StatusCode ?? 400;
        var code = exception is DomainException ? "INVALID_FILTER" : exception.ErrorCode;
        var errors = (exception as DiscoveryException)?.Errors;
        context.Result = new ObjectResult(new ErrorResponse(exception.Message, code, errors) { StatusCode = status }) { StatusCode = status };
        context.ExceptionHandled = true;
    }

    public static bool IsDiscoveryAction(Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor descriptor) =>
        descriptor is ControllerActionDescriptor action && action.ControllerTypeInfo.Name is
            "DiscoveryController" or "AdminCoffeeClassificationController" or "DiscoveryFavoritesController";
}
