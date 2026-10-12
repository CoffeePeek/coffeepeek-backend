using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Shops.Application.Features.CoffeeCatalog;

public sealed class CoffeeCatalogException(string message, int statusCode = 400)
    : BaseException(message, "coffee_catalog_error", statusCode);
