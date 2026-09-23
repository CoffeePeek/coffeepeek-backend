using System.ComponentModel.DataAnnotations;
using CoffeePeek.Shops.Domain;

namespace CoffeePeek.Shops.Application.Features.CoffeeShop.GetShopsInBounds;

public record GetShopsInBoundsQuery(
    [Range(-BusinessConstants.MaxLocationLatitude, BusinessConstants.MaxLocationLatitude)] decimal MinLat,
    [Range(-BusinessConstants.MaxLocationLongitude, BusinessConstants.MaxLocationLongitude)] decimal MinLon,
    [Range(-BusinessConstants.MaxLocationLatitude, BusinessConstants.MaxLocationLatitude)] decimal MaxLat,
    [Range(-BusinessConstants.MaxLocationLongitude, BusinessConstants.MaxLocationLongitude)] decimal MaxLon,
    [Range(0, 22)] int? Zoom = null
);
