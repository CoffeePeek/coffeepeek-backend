using CoffeePeek.Moderation.Domain.Aggregates.Enums;
using CoffeePeek.Moderation.Domain.Common.Enums;
using CoffeePeek.Moderation.Domain.Entities;
using CoffeePeek.Shared.Domain.Entities;

namespace CoffeePeek.Moderation.Domain.Aggregates;

public partial class ModerationShop : Entity<Guid>
{
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public PriceRange? PriceRange { get; private set; }
    public CoffeeFocus? CoffeeFocus { get; private set; }
    public ModerationStatus ModerationStatus { get; private set; }
    public string? RejectedReason { get; private set; }
    
    public Guid? UserId { get; private init; }
    public Guid CityId { get; private set; }
    public Guid ShopId { get; private set; }
    
    public ModerationShopContact? Contact { get; private set; }
    public ModerationLocation? Location { get; private set; }

    private readonly List<PhotoMetadata> _shopPhotos = [];
    public IReadOnlyCollection<PhotoMetadata> ShopPhotos => _shopPhotos.AsReadOnly();

    private readonly List<ModerationShopSchedule> _schedules = [];
    public IReadOnlyCollection<ModerationShopSchedule> Schedules => _schedules.AsReadOnly();

    private readonly List<ModerationShopEquipment> _moderationShopEquipments = [];
    public IReadOnlyCollection<ModerationShopEquipment> ModerationShopEquipments => _moderationShopEquipments.AsReadOnly();

    private readonly List<ModerationCoffeeBeanShop> _moderationCoffeeBeanShops = [];
    public IReadOnlyCollection<ModerationCoffeeBeanShop> ModerationCoffeeBeanShops => _moderationCoffeeBeanShops.AsReadOnly();
    private readonly List<ModerationShopRoaster> _moderationRoasterShops = [];
    public IReadOnlyCollection<ModerationShopRoaster> ModerationRoasterShops => _moderationRoasterShops.AsReadOnly();
    private readonly List<ModerationShopBrewMethod> _moderationShopBrewMethods = [];
    public IReadOnlyCollection<ModerationShopBrewMethod> ModerationShopBrewMethods => _moderationShopBrewMethods.AsReadOnly();

    public MenuDraftAggregate.MenuDraft? Menu { get; private set; }

    private ModerationShop() { }
}
