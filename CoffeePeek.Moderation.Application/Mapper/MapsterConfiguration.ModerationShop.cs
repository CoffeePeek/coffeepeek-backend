using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Dtos.Shop;
using CoffeePeek.Moderation.Application.Features.Menu;
using CoffeePeek.Moderation.Domain.Aggregates;
using CoffeePeek.Shared.Kernel.Options;
using Mapster;
using ContractCoffeeShopType = CoffeePeek.Contract.Enums.CoffeeShopType;
using ModerationShop = CoffeePeek.Moderation.Domain.Aggregates.ModerationShop;

namespace CoffeePeek.Moderation.Application.Mapper;

public partial class MapsterConfiguration
{
    private static void ConfigureModerationShop(TypeAdapterConfig config, MediaPublicUrlOptions mediaOptions)
    {
        config.NewConfig<ModerationShop, ModerationShopDto>()
            .Map(dest => dest.Address, src => src.Location == null ? null : src.Location.Address)
            .Map(dest => dest.AddressIsValidated, src => src.Location != null && src.Location.IsAddressValidated)
            .Map(dest => dest.Type, src => src.CoffeeFocus == null
                ? (ContractCoffeeShopType?)null
                : (ContractCoffeeShopType)(int)src.CoffeeFocus.Value)
            .Map(dest => dest.ShopPhotos, src => src.ShopPhotos)
            .Map(dest => dest.ShopContact, src => src.Contact)
            .Map(d => d.EquipmentIds, s => s.ModerationShopEquipments.Select(x => x.EquipmentId))
            .Map(d => d.CoffeeBeanIds, s => s.ModerationCoffeeBeanShops.Select(x => x.CoffeeBeanId))
            .Map(d => d.RoasterIds, s => s.ModerationRoasterShops.Select(x => x.RoasterId))
            .Map(d => d.BrewMethodIds, s => s.ModerationShopBrewMethods.Select(x => x.BrewMethodId))
            .Ignore(dest => dest.Menu)
            .AfterMapping((src, dest) => dest.Menu = MenuDraftMapper.ToDto(src.Menu, mediaOptions));
        
        config.NewConfig<ModerationShop, ShopDto>()
            .Map(d => d.Photos, s => s.ShopPhotos)
            .Map(d => d.Rating, s => 0)
            .Map(d => d.ReviewCount, s => 0)
            .Map(d => d.IsOpen, s => true)
            .Map(dest => dest.Type, src => src.CoffeeFocus == null
                ? (ContractCoffeeShopType?)null
                : (ContractCoffeeShopType)(int)src.CoffeeFocus.Value)
            .Map(d => d.CoffeeBeans, s => s.ModerationCoffeeBeanShops)
            .Map(d => d.Roasters, s => s.ModerationRoasterShops)
            .Map(d => d.Equipments, s => s.ModerationShopEquipments)
            .Map(d => d.BrewMethods, s => s.ModerationShopBrewMethods)
            .Map(d => d.ShopContact, s => s.Contact)
            .Map(d => d.Schedules, s => s.Schedules)
            .Ignore(dest => dest.Menu)
            .AfterMapping((src, dest) => dest.Menu = MenuDraftMapper.ToDto(src.Menu, mediaOptions));
        
        config.NewConfig<ModerationShopRoaster, RoasterDto>()
            .Map(dest => dest.Id, src => src.RoasterId);
        config.NewConfig<ModerationShopEquipment, EquipmentDto>()
            .Map(dest => dest.Id, src => src.EquipmentId);
        config.NewConfig<ModerationShopBrewMethod, BrewMethodDto>()
            .Map(dest => dest.Id, src => src.BrewMethodId);
        config.NewConfig<ModerationCoffeeBeanShop, CoffeeBeansDto>()
            .Map(dest => dest.Id, src => src.CoffeeBeanId);
    }
}