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
        if (cityId == Guid.Empty)
            throw new DomainException("CityId is required.");
        ValidateDescription(description);
        if (coffeeFocus.HasValue && !Enum.IsDefined(coffeeFocus.Value))
            throw new DomainException("Unknown coffee shop type.");

        return new ModerationShop
        {
            Id = Guid.NewGuid(),
            Name = ValidateName(name),
            UserId = userId,
            CityId = cityId,
            Description = description,
            CoffeeFocus = coffeeFocus,
            ModerationStatus = ModerationStatus.Pending,
        };
    }

    private static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Name is required");

        var trimmed = name.Trim();
        if (trimmed.Length > BusinessConstants.MaxShopNameLength)
            throw new DomainException($"Name cannot exceed {BusinessConstants.MaxShopNameLength} characters.");
        return trimmed;
    }

    public void SetLocation(ModerationLocation moderationLocation)
    {
        EnsureEditable();
        Location = moderationLocation;
    }

    public void AddPhoto(string fileName, string contentType, string storageKey, long length)
    {
        EnsureEditable();
        var photo = PhotoMetadata.Create(fileName, contentType, storageKey, length, UserId, Id);
        _shopPhotos.Add(photo);
    }

    /// <summary>Removes gallery photos whose storage key is not in <paramref name="keptStorageKeys"/>.</summary>
    /// <returns>The removed photos, so the caller can release their storage objects.</returns>
    public IReadOnlyList<PhotoMetadata> RemovePhotosExcept(IReadOnlySet<string> keptStorageKeys)
    {
        EnsureEditable();
        var removed = _shopPhotos.Where(p => !keptStorageKeys.Contains(p.StorageKey)).ToList();
        _shopPhotos.RemoveAll(p => !keptStorageKeys.Contains(p.StorageKey));
        return removed;
    }

    public void AttachMenuPhotos(
        IReadOnlyList<(string FileName, string ContentType, string StorageKey, long SizeBytes)> photos,
        DateTime utcNow)
    {
        EnsureMenuEditable();
        Menu ??= MenuDraftAggregate.MenuDraft.CreateEmpty();
        Menu.AttachPhotos(photos, utcNow);
    }

    public void RequestMenuParse()
    {
        EnsureMenuEditable();
        if (Menu is null || Menu.Photos.Count == 0)
            throw new DomainException("Attach menu photos before parsing.");

        Menu.MarkParsePending();
    }

    public bool ApplyMenuParseResult(
        bool success,
        string? error,
        int? suggestedPriceRange,
        IReadOnlyList<MenuDraftAggregate.MenuDraftItem> items,
        IReadOnlyList<MenuDraftAggregate.MenuDraftUnmatched> unmatched,
        DateTime utcNow,
        Guid? requestId = null)
    {
        return Menu?.ApplyParseResult(success, error, suggestedPriceRange, items, unmatched, utcNow, requestId) == true;
    }

    public void ReplaceMenuItems(
        IReadOnlyList<MenuDraftAggregate.MenuDraftItem> items,
        DateTime utcNow)
    {
        EnsureMenuEditable();
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
        EnsureEditable();
        var validatedName = name is null ? null : ValidateName(name);
        ValidateDescription(description);
        if (cityId == Guid.Empty)
            throw new DomainException("CityId is required.");
        if (priceRange.HasValue && !Enum.IsDefined(priceRange.Value))
            throw new DomainException("Unknown price range.");
        if (coffeeFocus.HasValue && !Enum.IsDefined(coffeeFocus.Value))
            throw new DomainException("Unknown coffee shop type.");

        if (name != null)
            Name = validatedName!;

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
        EnsureMenuEditable();
        if (!Enum.IsDefined(suggested))
            throw new DomainException("Unknown price range.");
        PriceRange = suggested;
    }

    public void UpdateContacts(string? phone, string? instagram, string? email, string? site)
    {
        EnsureEditable();
        Contact = ModerationShopContact.Create(phone, instagramLink: instagram, email, site);
    }

    public void UpdateSchedules(IEnumerable<(DayOfWeek DayOfWeek, List<(TimeSpan OpenTime, TimeSpan CloseTime)> Intervals)> schedules)
    {
        UpdateSchedules(schedules.Select(schedule =>
            (schedule.DayOfWeek, schedule.Intervals.Count == 0, schedule.Intervals)));
    }

    public void UpdateSchedules(
        IEnumerable<(DayOfWeek DayOfWeek, bool IsClosed, List<(TimeSpan OpenTime, TimeSpan CloseTime)> Intervals)> schedules)
    {
        EnsureEditable();
        var replacements = new List<ModerationShopSchedule>();
        foreach (var schedule in schedules)
        {
            var intervals = schedule.Intervals
                .Select(i => new ModerationShopScheduleInterval(i.OpenTime, i.CloseTime))
                .ToList()
                .AsReadOnly();

            replacements.Add(new ModerationShopSchedule(schedule.DayOfWeek, schedule.IsClosed, intervals));
        }
        if (replacements.Select(s => s.DayOfWeek).Distinct().Count() != replacements.Count)
            throw new DomainException("Schedule days must be unique.");
        _schedules.Clear();
        _schedules.AddRange(replacements);
    }

    public void UpdateRelations(
        List<Guid>? equipmentIds,
        List<Guid>? coffeeBeanIds,
        List<Guid>? roasterIds,
        List<Guid>? brewMethodIds)
    {
        EnsureEditable();
        if (new[] { equipmentIds, coffeeBeanIds, roasterIds, brewMethodIds }
            .Any(ids => ids?.Contains(Guid.Empty) == true))
            throw new DomainException("Related IDs cannot be empty.");
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
        RejectedReason = null;
        return true;
    }

    public bool Reject(string reason)
    {
        // An approved shop is already published to the catalog; rejecting it here would leave it live.
        if (ModerationStatus == ModerationStatus.Approved)
            throw new DomainException("Approved shop is already published and cannot be rejected.");

        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("Reject reason is required.");

        reason = reason.Trim();
        if (reason.Length is < BusinessConstants.MinRejectReasonCommentLength or > BusinessConstants.MaxRejectReasonCommentLength)
            throw new DomainException(
                $"{nameof(reason)} must be between {BusinessConstants.MinRejectReasonCommentLength} and {BusinessConstants.MaxRejectReasonCommentLength} characters.");

        if (ModerationStatus == ModerationStatus.Rejected && RejectedReason == reason)
            return false;
        ModerationStatus = ModerationStatus.Rejected;
        RejectedReason = reason;
        return true;
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
        if (ShopId != Guid.Empty && ShopId != shopId)
            throw new DomainException("The submission is already linked to another published shop.");

        ShopId = shopId;
    }

    public void AddPriceRange(PriceRange priceRange)
    {
        EnsureEditable();
        if (!Enum.IsDefined(priceRange))
            throw new DomainException("Unknown price range.");
        PriceRange = priceRange;
    }

    public void AddCoffeeFocus(CoffeeFocus coffeeFocus)
    {
        EnsureEditable();
        if (!Enum.IsDefined(coffeeFocus))
            throw new DomainException("Unknown coffee shop type.");
        CoffeeFocus = coffeeFocus;
    }

    private void EnsureEditable()
    {
        if (ModerationStatus == ModerationStatus.Approved)
            throw new DomainException("Approved shop is already published; submit a shop change request instead.");
    }

    private void EnsureMenuEditable()
    {
        if (ModerationStatus == ModerationStatus.Approved && ShopId == Guid.Empty)
            throw new DomainException("Wait for shop publication to complete before editing its menu.");
    }

    private static void ValidateDescription(string? description)
    {
        if (description?.Length > BusinessConstants.MaxShopDescriptionLength)
            throw new DomainException($"Description cannot exceed {BusinessConstants.MaxShopDescriptionLength} characters.");
    }
}
