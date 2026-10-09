using CoffeePeek.Contract.Dtos.Coffee;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeProductAggregate;

namespace CoffeePeek.Shops.Application.Features.CoffeeCatalog;

public static class CoffeeContentMapper
{
    public static CoffeeContent ToDomain(CoffeeContentDto data)
    {
        if (data is null || new[] { data.TasteDescriptors, data.BrewRecommendations, data.GrindOptions, data.Features }
            .Any(values => values is null || values.Length > 100 || values.Any(v => v is null || v.Length > 500)))
            throw new CoffeeCatalogException("Invalid coffee content.");
        if (new[] { data.Processing, data.RoastLevel, data.Acidity, data.Body, data.QGraderScoreRaw }.Any(v => v?.Length > 1000))
            throw new CoffeeCatalogException("Coffee characteristic is too long.");
        return new(data.Name, data.Description, data.ProductKind, data.ProductForm, data.CompositionKind, data.Processing,
            data.RoastLevel, data.Acidity, data.Body, data.QGraderScoreRaw,
            data.TasteDescriptors, data.BrewRecommendations, data.GrindOptions, data.Features);
    }
}
