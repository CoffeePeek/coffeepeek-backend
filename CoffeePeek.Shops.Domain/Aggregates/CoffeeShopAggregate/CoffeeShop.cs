using CoffeePeek.Shared.Domain.PublicAddresses;
using CoffeePeek.Shared.Domain.Entities;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Domain.Aggregates.BrewMethods;
using CoffeePeek.Shops.Domain.Entities;
using FluentResults;

namespace CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;

public sealed class CoffeeShop : Entity<Guid>, IPublicSlugOwner
{
    public string? PublicSlug { get; private set; }
    public long SlugRevision { get; private set; }

    public void SetPublicSlug(string slug)
    {
        CoffeePeek.Shared.Domain.PublicAddresses.PublicSlug.Validate(slug);
        if (PublicSlug == slug) return;
        PublicSlug = slug;
        SlugRevision = checked(SlugRevision + 1);
    }


    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public PriceRange PriceRange { get; private set; }
    public CoffeeFocusType? Type { get; private set; }
    public CoffeeShopStatus Status { get; private set; } = CoffeeShopStatus.Active;
    public long ProfileRevision { get; private set; }

    public void AdvanceProfileRevision() => ProfileRevision = checked(ProfileRevision + 1);

    /// <summary>
    /// Admin moderation flag, independent of the business status. Hiding used to overload TemporarilyClosed,
    /// which also means a real closure and an imported draft, so un-hiding could reopen a closed shop.
    /// </summary>
    public bool IsHidden { get; private set; }
    public short DataCompletenessScore { get; private set; }

    public Guid? CreatorId { get; private set; }
    public Guid? OwnerUserId { get; private set; }
    public Guid? ModerationId {get; private set; }
    public DateTime? ImportedFromFileAt { get; private set; }

    public ShopContact Contact { get; private set; }
    public Location Location { get; private set; }

    private readonly List<ShopSchedule> _schedules = [];
    public IReadOnlyCollection<ShopSchedule> Schedules => _schedules.AsReadOnly();

    private readonly List<ShopPhoto> _shopPhotos = [];
    public IReadOnlyCollection<ShopPhoto> ShopPhotos => _shopPhotos.AsReadOnly();

    private readonly List<ShopEquipment> _shopEquipments = [];
    public IReadOnlyCollection<ShopEquipment> ShopEquipments => _shopEquipments.AsReadOnly();
    public IReadOnlyCollection<Equipment> Equipments => _shopEquipments.Select(x => x.Equipment).ToArray();

    private readonly List<CoffeeBean> _coffeeBeans = [];
    public IReadOnlyCollection<CoffeeBean> CoffeeBeans => _coffeeBeans.AsReadOnly();

    private readonly List<Roaster> _roasters = [];
    public IReadOnlyCollection<Roaster> Roasters => _roasters.AsReadOnly();

    private readonly List<BrewMethod> _brewMethods = [];
    public IReadOnlyCollection<BrewMethod> BrewMethods => _brewMethods.AsReadOnly();

    private readonly List<CoffeeShopTag> _shopTags = [];
    public IReadOnlyCollection<CoffeeShopTag> ShopTags => _shopTags.AsReadOnly();

    // ReSharper disable once UnusedMember.Local
    private CoffeeShop()
    {
    }

    public CoffeeShop(Guid? creatorId, string name, string? description, PriceRange priceRange, Guid moderationId)
    {
        ThrowIfInvalid(ValidateDetails(name, description, priceRange));
        Id = Guid.NewGuid();
        CreatorId = creatorId;
        Name = name;
        Description = description;
        PriceRange = priceRange;
        ModerationId = moderationId;
    }

    #region Domain Logic

    public bool IsNew => CreatedAtUtc > DateTime.UtcNow.AddDays(-BusinessConstants.ItNewEntityInDays);
    public bool? IsOpen => IsOpenAtUtc(DateTime.UtcNow);

    /// <summary>
    /// Evaluates the shop against a weekly schedule expressed entirely in UTC.
    /// </summary>
    public bool? IsOpenAtUtc(DateTime utcNow)
    {
        if (utcNow.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Schedule must be evaluated with a UTC DateTime.", nameof(utcNow));

        switch (Status)
        {
            case CoffeeShopStatus.PermanentlyClosed:
            case CoffeeShopStatus.TemporarilyClosed:
                return false;
            case CoffeeShopStatus.Active:
                break;
        }

        return ShopScheduleEvaluator.IsOpenAtUtc(Schedules, utcNow);
    }
    
    public static Result ValidateDetails(string? name, string? description, PriceRange priceRange)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Fail("Shop name is required.");

        if (name.Length > BusinessConstants.MaxCoffeeShopNameLength)
            return Result.Fail($"Shop name cannot exceed {BusinessConstants.MaxCoffeeShopNameLength} characters.");

        if (description?.Length > BusinessConstants.MaxCoffeeShopDescriptionLength)
            return Result.Fail($"Shop description cannot exceed {BusinessConstants.MaxCoffeeShopDescriptionLength} characters.");

        return ValidatePriceRange(priceRange);
    }

    public static Result ValidatePriceRange(PriceRange priceRange) =>
        Enum.IsDefined(priceRange) ? Result.Ok() : Result.Fail("Unknown shop price range.");

    public static Result ValidateStatus(CoffeeShopStatus status) =>
        Enum.IsDefined(status) ? Result.Ok() : Result.Fail("Unknown shop status.");

    public static Result ValidateCoffeeFocus(CoffeeFocusType? focus) =>
        focus is null || Enum.IsDefined(focus.Value) ? Result.Ok() : Result.Fail("Unknown shop coffee focus.");

    private static void ThrowIfInvalid(Result validation)
    {
        if (validation.IsFailed)
            throw new DomainException(validation.Errors[0].Message);
    }

    public void UpdateDetails(string name, string? description, PriceRange priceRange)
    {
        ThrowIfInvalid(ValidateDetails(name, description, priceRange));
        Name = name;
        Description = description;
        PriceRange = priceRange;
    }

    public void SetPriceRange(PriceRange priceRange)
    {
        ThrowIfInvalid(ValidatePriceRange(priceRange));
        PriceRange = priceRange;
    }

    public void SetStatus(CoffeeShopStatus status)
    {
        ThrowIfInvalid(ValidateStatus(status));
        Status = status;
    }

    public void SetCoffeeFocus(CoffeeFocusType? focus)
    {
        ThrowIfInvalid(ValidateCoffeeFocus(focus));
        Type = focus;
    }

    public void MarkImportedFromFile(DateTime utcNow)
    {
        ImportedFromFileAt ??= utcNow;
    }

    public void SetHidden(bool hidden)
    {
        IsHidden = hidden;
    }

    public void AssignOwner(Guid? ownerUserId)
    {
        OwnerUserId = ownerUserId == Guid.Empty ? null : ownerUserId;
    }

    public void SetLocation(Guid cityId, string address, decimal latitude, decimal longitude)
    {
        Location = Location.CreateValidated(cityId, address, latitude, longitude);
    }

    /// <summary>Address without coordinates (not geocoded); the shop stays off the map until coordinates are set.</summary>
    public void SetDraftLocation(Guid cityId, string address)
    {
        Location = Location.CreateDraft(cityId, address);
    }

    public void SetContact(string? instagramLink, string? email, string? siteLink, string? phoneNumber)
    {
        Contact = ShopContact.Create(instagramLink, email, siteLink, phoneNumber);
    }

    /// <summary>
    /// Fills empty contact/address fields from an import dump. Never overwrites a non-empty value
    /// with a weaker one, and never creates a second shop.
    /// </summary>
    public bool TryEnrichFromImport(
        string? address,
        string? instagram,
        string? website,
        string? phone)
    {
        var changed = false;
        var current = Contact;
        var nextInstagram = FirstNonEmpty(current?.InstagramLink, Clip(instagram, BusinessConstants.MaxShopContactInstagramLinkLength));
        var nextWebsite = FirstNonEmpty(current?.SiteLink, Clip(website, BusinessConstants.MaxShopContactSiteLinkLength));
        var nextPhone = FirstNonEmpty(current?.PhoneNumber, ClipPhone(phone));
        var nextEmail = current?.Email;

        if (nextInstagram != current?.InstagramLink
            || nextWebsite != current?.SiteLink
            || nextPhone != current?.PhoneNumber)
        {
            SetContact(nextInstagram, nextEmail, nextWebsite, nextPhone);
            changed = true;
        }

        if (Location is not null)
        {
            var nextAddress = PreferRicherAddress(Location.Address, address);
            if (!string.Equals(nextAddress, Location.Address, StringComparison.Ordinal)
                && Location.Latitude is not null
                && Location.Longitude is not null)
            {
                SetLocation(Location.CityId, nextAddress, Location.Latitude.Value, Location.Longitude.Value);
                changed = true;
            }
        }

        return changed;
    }

    private static string PreferRicherAddress(string current, string? incoming)
    {
        if (string.IsNullOrWhiteSpace(incoming))
            return current;

        var incomingTrim = incoming.Trim();
        if (string.IsNullOrWhiteSpace(current)
            || current.Equals("Минск", StringComparison.OrdinalIgnoreCase)
            || current.Equals("Minsk", StringComparison.OrdinalIgnoreCase))
            return incomingTrim;

        if (incomingTrim.Length > current.Trim().Length
            && incomingTrim.Contains(current.Trim(), StringComparison.OrdinalIgnoreCase))
            return incomingTrim;

        return current;
    }

    private static string? FirstNonEmpty(string? current, string? incoming) =>
        string.IsNullOrWhiteSpace(current) ? incoming : current;

    private static string? Clip(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static string? ClipPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return null;

        var first = phone.Split(';', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        return Clip(first, BusinessConstants.MaxShopContactPhoneNumberLength);
    }
    
    public void AddPhotos(IEnumerable<ShopPhoto> photos)
    {
        ArgumentNullException.ThrowIfNull(photos);
        var additions = photos.ToArray();
        var combined = _shopPhotos.Concat(additions).ToArray();
        if (additions.Any(p => p is null || p.Id == Guid.Empty) ||
            combined.Select(p => p.Id).Distinct().Count() != combined.Length ||
            combined.Select(p => p.StorageKey).Distinct(StringComparer.Ordinal).Count() != combined.Length)
            throw new DomainException("Gallery photos must have distinct IDs and storage keys.");
        if (additions.Length == 0) return;
        var nextIndex = _shopPhotos.Count == 0
            ? 0
            : checked(_shopPhotos.Max(p => p.SortIndex) + 1);
        _ = checked(nextIndex + additions.Length - 1);

        foreach (var photo in additions)
        {
            photo.SetSortIndex(nextIndex++);
            _shopPhotos.Add(photo);
        }
    }

    /// <summary>
    /// Removes gallery photos by id and reindexes remaining <see cref="ShopPhoto.SortIndex"/> values.
    /// </summary>
    public Result RemovePhotos(IReadOnlyList<Guid> photoIds)
    {
        if (photoIds is null || photoIds.Count == 0)
            return Result.Ok();

        if (photoIds.Distinct().Count() != photoIds.Count)
            return Result.Fail("Photo list contains duplicate photo IDs.");

        var byId = _shopPhotos.ToDictionary(p => p.Id);
        foreach (var id in photoIds)
        {
            if (!byId.ContainsKey(id))
                return Result.Fail($"Photo '{id}' does not belong to this shop.");
        }

        foreach (var id in photoIds)
            _shopPhotos.Remove(byId[id]);

        var remaining = _shopPhotos.OrderBy(p => p.SortIndex).ToList();
        for (var i = 0; i < remaining.Count; i++)
            remaining[i].SetSortIndex(i);

        return Result.Ok();
    }

    /// <summary>
    /// Reorders gallery photos. <paramref name="orderedPhotoIds"/> must be a full permutation
    /// of the shop's current photo IDs; first ID becomes SortIndex 0 (cover).
    /// </summary>
    public Result ReorderPhotos(IReadOnlyList<Guid> orderedPhotoIds)
    {
        if (orderedPhotoIds is null)
            return Result.Fail("Photo order is required.");

        if (orderedPhotoIds.Count != _shopPhotos.Count)
            return Result.Fail("Photo order must include every gallery photo exactly once.");

        if (orderedPhotoIds.Distinct().Count() != orderedPhotoIds.Count)
            return Result.Fail("Photo order contains duplicate photo IDs.");

        var byId = _shopPhotos.ToDictionary(p => p.Id);
        foreach (var id in orderedPhotoIds)
        {
            if (!byId.ContainsKey(id))
                return Result.Fail($"Photo '{id}' does not belong to this shop.");
        }

        for (var i = 0; i < orderedPhotoIds.Count; i++)
            byId[orderedPhotoIds[i]].SetSortIndex(i);

        return Result.Ok();
    }
    
    public void AddEquipment(Equipment equipment)
    {
        if (_shopEquipments.Any(e => e.EquipmentId == equipment.Id
                                    || e.Equipment.Brand == equipment.Brand && e.Equipment.ModelName == equipment.ModelName))
            return;

        _shopEquipments.Add(new ShopEquipment(Id, equipment));
    }

    public void RemoveEquipment(Guid equipmentId)
    {
        var assignment = _shopEquipments.FirstOrDefault(e => e.EquipmentId == equipmentId);
        if (assignment is not null)
            _shopEquipments.Remove(assignment);
    }

    /// <summary>
    /// Replaces the shop's equipment set. Existing primary choices survive for retained
    /// equipment unless an explicit per-category selection is supplied.
    /// </summary>
    public Result SetEquipment(IEnumerable<Equipment> equipment, IReadOnlyCollection<Guid>? primaryEquipmentIds = null)
    {
        var selected = equipment.DistinctBy(e => e.Id).DistinctBy(e => (e.Brand, e.ModelName)).ToArray();
        var primaryIds = primaryEquipmentIds?.ToArray()
            ?? _shopEquipments.Where(e => e.IsPrimary && selected.Any(s => s.Id == e.EquipmentId))
                .Select(e => e.EquipmentId).ToArray();
        var validation = ValidateEquipmentSelection(selected, primaryIds);
        if (validation.IsFailed)
            return validation;

        var selectedIds = selected.Select(e => e.Id).ToHashSet();
        _shopEquipments.RemoveAll(e => !selectedIds.Contains(e.EquipmentId));
        foreach (var item in selected)
        {
            if (_shopEquipments.All(e => e.EquipmentId != item.Id))
                _shopEquipments.Add(new ShopEquipment(Id, item));
        }
        var chosen = primaryIds.ToHashSet();
        foreach (var assignment in _shopEquipments)
            assignment.SetPrimary(chosen.Contains(assignment.EquipmentId));
        return Result.Ok();
    }

    public static Result ValidateEquipmentSelection(
        IReadOnlyCollection<Equipment> equipment, IReadOnlyCollection<Guid> primaryEquipmentIds)
    {
        if (primaryEquipmentIds.Count != primaryEquipmentIds.Distinct().Count())
            return Result.Fail("Primary equipment IDs must be distinct.");
        var selected = equipment.ToDictionary(e => e.Id);
        if (primaryEquipmentIds.Any(id => !selected.ContainsKey(id)))
            return Result.Fail("Primary equipment must belong to this shop.");
        if (primaryEquipmentIds.GroupBy(id => selected[id].CategoryId).Any(group => group.Count() > 1))
            return Result.Fail("Only one primary equipment is allowed per category.");
        return Result.Ok();
    }
    
    public void SetBrewMethods(IEnumerable<BrewMethod> methods)
    {
        ArgumentNullException.ThrowIfNull(methods);
        var replacement = methods.ToArray();
        _brewMethods.Clear();
        _brewMethods.AddRange(replacement);
    }

    public void SetRoasters(IEnumerable<Roaster> roasters)
    {
        ArgumentNullException.ThrowIfNull(roasters);
        var replacement = roasters.ToArray();
        _roasters.Clear();
        _roasters.AddRange(replacement);
    }

    public void SetBeans(IEnumerable<CoffeeBean> beans)
    {
        ArgumentNullException.ThrowIfNull(beans);
        var replacement = beans.ToArray();
        _coffeeBeans.Clear();
        _coffeeBeans.AddRange(replacement);
    }
    
    public void AddSchedule(List<ShopSchedule> schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ThrowIfInvalid(ReplaceSchedules([.. _schedules, .. schedule]));
    }

    /// <summary>
    /// Replaces the weekly schedule. Duplicate days are rejected. Empty list means no hours stored
    /// and the opening state of an active shop is unknown.
    /// </summary>
    public Result ReplaceSchedules(IReadOnlyList<ShopSchedule> schedules)
    {
        if (schedules is null)
            return Result.Fail("Schedule is required.");

        var replacement = schedules.ToArray();
        if (replacement.Any(s => s is null || !Enum.IsDefined(s.DayOfWeek) || s.Intervals.Any(i => i is null)))
            return Result.Fail("Schedule contains an invalid day or interval.");
        var days = replacement.Select(s => s.DayOfWeek).ToList();
        if (days.Distinct().Count() != days.Count)
            return Result.Fail("Schedule contains duplicate days of week.");

        _schedules.Clear();
        _schedules.AddRange(replacement);
        return Result.Ok();
    }

    /// <summary>
    /// Replaces the shop's tag set. Tag IDs are deduplicated; max
    /// <see cref="BusinessConstants.MaxShopTagsPerShop"/> enforced.
    /// </summary>
    public void SetTags(IReadOnlyList<Guid> tagIds, Guid? assignedByUserId)
    {
        ArgumentNullException.ThrowIfNull(tagIds);

        // null = system/anonymous-originated tagging (e.g. approved anonymous submission); Guid.Empty is still invalid.
        if (assignedByUserId == Guid.Empty)
            throw new DomainException("AssignedByUserId is required.");

        var distinct = tagIds.Distinct().ToList();
        if (distinct.Count > BusinessConstants.MaxShopTagsPerShop)
            throw new DomainException(
                $"A shop cannot have more than {BusinessConstants.MaxShopTagsPerShop} tags.");

        if (distinct.Contains(Guid.Empty))
            throw new DomainException("TagId cannot be empty.");

        _shopTags.Clear();
        var now = DateTime.UtcNow;
        foreach (var tagId in distinct)
        {
            _shopTags.Add(new CoffeeShopTag(Id, tagId, assignedByUserId, now));
        }
    }
    
    #endregion
}
