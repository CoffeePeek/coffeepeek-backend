namespace CoffeePeek.ShopsService.Controllers.Admin;

public record UpdateShopLocationRequest(Guid CityId, string Address, decimal Latitude, decimal Longitude);
