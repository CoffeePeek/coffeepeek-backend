using CoffeePeek.Moderation.Domain.Aggregates.Enums;
using CoffeePeek.Moderation.Domain.Common.Enums;
using CoffeePeek.Moderation.Domain.Entities;
using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Moderation.Domain.Aggregates;

public sealed partial class ModerationShop
{
    public static ModerationShop Create(
        string name,
        Guid? userId,
        Guid cityId,
        string? description,
        CoffeeFocus? coffeeFocus = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Name is required");

        return new ModerationShop
        {
            Id = Guid.NewGuid(),
            Name = name,
            UserId = userId,
            CityId = cityId,
            Description = description,
            CoffeeFocus = coffeeFocus,
            ModerationStatus = ModerationStatus.Pending,
        };
    }

    public void SetLocation(ModerationLocation moderationLocation)
    {
        Location = moderationLocation;
    }

    public void AddPhoto(string fileName, string contentType, string storageKey, long length)
    {
        var photo = PhotoMetadata.Create(fileName, contentType, storageKey, length, UserId, Id);
        _shopPhotos.Add(photo);
    }

    public void AttachMenuPhotos(
        IReadOnlyList<(string FileName, string ContentType, string StorageKey, long SizeBytes)> photos,
        DateTime utcNow)
    {
        Menu ??= MenuDraftAggregate.MenuDraft.CreateEmpty();
        Menu.AttachPhotos(photos, utcNow);
    }

    public void RequestMenuParse()
    {
        if (Menu is null || Menu.Photos.Count == 0)
            throw new DomainException("Attach menu photos before parsing.");

        Menu.MarkParsePending();
    }

    public void ApplyMenuParseResult(
        bool success,
        string? error,
        int? suggestedPriceRange,
        IReadOnlyList<MenuDraftAggregate.MenuDraftItem> items,
        IReadOnlyList<MenuDraftAggregate.MenuDraftUnmatched> unmatched,
        DateTime utcNow)
    {
        Menu ??= MenuDraftAggregate.MenuDraft.CreateEmpty();
        Menu.ApplyParseResult(success, error, suggestedPriceRange, items, unmatched, utcNow);
    }

    public void ReplaceMenuItems(
        IReadOnlyList<MenuDraftAggregate.MenuDraftItem> items,
        DateTime utcNow)
    {
        Menu ??= MenuDraftAggregate.MenuDraft.CreateEmpty();
        Menu.ApplyManualItems(items, utcNow);
    }
    
    public void UpdateInfo(
        string? name,
        string? description,
        PriceRange? priceRange,
        Guid? cityId,
        CoffeeFocus? coffeeFocus)
    {
        if (name != null) 
            Name = name;
        
        if (description != null) 
            Description = description;
        
        if (priceRange.HasValue) 
            PriceRange = priceRange.Value;
        
        if (cityId.HasValue) 
            CityId = cityId.Value;

        if (coffeeFocus.HasValue)
            CoffeeFocus = coffeeFocus.Value;
    }

    public void ApplySuggestedPriceRange(PriceRange suggested)
    {
        PriceRange = suggested;
    }

    public void UpdateContacts(string? phone, string? instagram, string? email, string? site)
    {
        Contact = ModerationShopContact.Create(phone, instagramLink: instagram, email, site);
    }

    public void UpdateSchedules(IEnumerable<(DayOfWeek DayOfWeek, List<(TimeSpan OpenTime, TimeSpan CloseTime)> Intervals)> schedules)
    {
        _schedules.Clear();
    
        foreach (var schedule in schedules)
        {
            var intervals = schedule.Intervals
                .Select(i => new ModerationShopScheduleInterval(i.OpenTime, i.CloseTime))
                .ToList()
                .AsReadOnly();
        
            var isClosed = schedule.Intervals.Count == 0;
        
            _schedules.Add(new ModerationShopSchedule(schedule.DayOfWeek, isClosed, intervals));
        }
    }

    public void UpdateRelations(
        List<Guid>? equipmentIds,
        List<Guid>? coffeeBeanIds,
        List<Guid>? roasterIds,
        List<Guid>? brewMethodIds)
    {
        UpdateCollection(_moderationShopEquipments, equipmentIds,
            id => new ModerationShopEquipment(Id, id),
            e => e.EquipmentId);
        
        UpdateCollection(_moderationCoffeeBeanShops, coffeeBeanIds, 
            id => new ModerationCoffeeBeanShop(Id, id),
            e => e.CoffeeBeanId);
        
        UpdateCollection(_moderationRoasterShops, roasterIds, 
            id => new ModerationShopRoaster(Id, id),
            e => e.RoasterId);
        
        UpdateCollection(_moderationShopBrewMethods, brewMethodIds, 
            id => new ModerationShopBrewMethod(Id, id),
            e => e.BrewMethodId);
    }
    
    public bool Approve()
    {
        if (ModerationStatus == ModerationStatus.Approved)
            return false;

        if (Location is null || !Location.IsAddressValidated)
            throw new DomainException("Cannot approve shop with unvalidated address.");

        ModerationStatus = ModerationStatus.Approved;
        return true;
    }

    public void Reject(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("Reject reason is required.");

        if (reason.Length is < BusinessConstants.MinRejectReasonCommentLength or > BusinessConstants.MaxRejectReasonCommentLength)
            throw new DomainException(
                $"{nameof(reason)} must be between {BusinessConstants.MinRejectReasonCommentLength} and {BusinessConstants.MaxRejectReasonCommentLength} characters.");

        ModerationStatus = ModerationStatus.Rejected;
        RejectedReason = reason;
    }

    private static void UpdateCollection<TJoinEntity>(
        List<TJoinEntity> currentCollection,
        IEnumerable<Guid>? newIds,
        Func<Guid, TJoinEntity> createFunc,
        Func<TJoinEntity, Guid> getIdFunc)
    {
        if (newIds == null) return;

        var newIdSet = newIds.ToHashSet();

        currentCollection.RemoveAll(item => !newIdSet.Contains(getIdFunc(item)));

        var currentIdSet = currentCollection.Select(getIdFunc).ToHashSet();
        var idsToAdd = newIdSet.Where(id => !currentIdSet.Contains(id));

        currentCollection.AddRange(idsToAdd.Select(createFunc));
    }

    public void AddShopId(Guid shopId)
    {
        if (shopId == Guid.Empty) 
            return;
        
        ShopId = shopId;
    }

    public void AddPriceRange(PriceRange priceRange)
    {
        PriceRange = priceRange;
    }

    public void AddCoffeeFocus(CoffeeFocus coffeeFocus)
    {
        CoffeeFocus = coffeeFocus;
    }
}