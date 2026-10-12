using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Dtos.Contact;
using CoffeePeek.Contract.Dtos.Schedule;
using CoffeePeek.Contract.Enums;
using CoffeePeek.Moderation.Domain;

namespace CoffeePeek.Moderation.Application.Features.Shop.CreateShop;

public record SendCoffeeShopToModerationCommand
{
    [JsonIgnore] public Guid? UserId { get; init; }

    [Required, MaxLength(BusinessConstants.MaxShopNameLength)] public string Name { get; init; } = null!;
    [Required] public string Address { get; init; } = null!;
    [Required] public Guid CityId { get; init; }

    public string? Description { get; init; }
    public PriceRange? PriceRange { get; init; }
    public CoffeeShopType? Type { get; init; }
    public ShopContactDto? ShopContact { get; init; }
    public List<ScheduleDto>? Schedules { get; init; }
    public List<Guid>? EquipmentIds { get; init; }
    public List<Guid>? CoffeeBeanIds { get; init; }
    public List<Guid>? RoasterIds { get; init; }
    public List<Guid>? BrewMethodIds { get; init; }
    public List<UploadedPhotoDto>? ShopPhotos { get; init; }
    public List<UploadedPhotoDto>? MenuPhotos { get; init; }
}
