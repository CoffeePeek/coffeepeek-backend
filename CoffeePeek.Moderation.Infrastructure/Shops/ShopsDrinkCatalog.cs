using System.Net.Http.Json;
using System.Text.Json;
using CoffeePeek.Contract.Dtos.Menu;
using CoffeePeek.Shared.Kernel.Drinks;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;

namespace CoffeePeek.Moderation.Infrastructure.Shops;

public sealed class ShopsDrinkCatalog(IHttpClientFactory clients) : IDrinkCatalog
{
    public async Task<DrinkCatalogEntry?> FindAsync(string slug, CancellationToken ct)
    {
        try
        {
            var response = await clients.CreateClient("shops-drink-catalog")
                .GetFromJsonAsync<Response<ConsumedDrinkOptionDto[]>>("/api/catalogs/drinks", ct);
            if (response?.IsSuccess != true || response.Data is null)
                throw new ServiceUnavailableException("Drink catalog could not be verified. Please try again.");
            var drink = response.Data.SingleOrDefault(d => d.Slug == slug);
            return drink is null ? null : new(drink.NameRu, drink.NameEn, true);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException ||
                                   ex is OperationCanceledException && !ct.IsCancellationRequested)
        {
            throw new ServiceUnavailableException("Drink catalog could not be verified. Please try again.", ex);
        }
    }
}
