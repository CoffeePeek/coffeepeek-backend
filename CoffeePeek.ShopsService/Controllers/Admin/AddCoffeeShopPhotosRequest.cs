using CoffeePeek.Contract.Dtos;

namespace CoffeePeek.ShopsService.Controllers.Admin;

public record AddCoffeeShopPhotosRequest(IReadOnlyList<UploadedPhotoDto> Photos);
