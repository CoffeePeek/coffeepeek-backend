using System.ComponentModel.DataAnnotations;
using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Dtos.Contact;
using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Contract.Dtos.Schedule;
using CoffeePeek.Contract.Enums;
using CoffeePeek.Moderation.Domain;

namespace CoffeePeek.ModerationService.Controllers;

public sealed record PublicSubmitShopRequest
{
    [Required, MaxLength(BusinessConstants.MaxShopNameLength)] public string Name { get; init; } = string.Empty;
    [Required] public string Address { get; init; } = string.Empty;
    [Required, MaxLength(100)] public string City { get; init; } = string.Empty;
    public string? Description { get; init; }
    public PriceRange? PriceRange { get; init; }
    public CoffeeShopType? Type { get; init; }
    public ShopContactDto? ShopContact { get; init; }
    public List<ScheduleDto>? Schedules { get; init; }
    [MaxLength(100)] public string[]? Equipments { get; init; }
    [MaxLength(100)] public string[]? Beans { get; init; }
    [MaxLength(100)] public string[]? Roasters { get; init; }
    [MaxLength(100)] public string[]? BrewMethods { get; init; }
    public List<UploadedPhotoDto>? ShopPhotos { get; init; }
    public List<UploadedPhotoDto>? MenuPhotos { get; init; }
}
