namespace CoffeePeek.Shops.Application.Features.CoffeeZones;

public record GenerateCoffeeZoneCandidatesQuery(Guid CityId, int RadiusMeters = 400, int MinShops = 4);
