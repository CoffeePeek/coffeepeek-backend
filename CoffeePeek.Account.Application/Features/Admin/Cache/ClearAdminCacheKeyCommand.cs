using System.ComponentModel.DataAnnotations;

namespace CoffeePeek.Account.Application.Features.Admin.Cache;

public record ClearAdminCacheKeyCommand([Required, MinLength(3)] string Key);
