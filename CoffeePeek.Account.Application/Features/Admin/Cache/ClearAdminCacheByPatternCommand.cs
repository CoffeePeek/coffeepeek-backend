using System.ComponentModel.DataAnnotations;

namespace CoffeePeek.Account.Application.Features.Admin.Cache;

public record ClearAdminCacheByPatternCommand([Required, MinLength(3)] string Pattern);
