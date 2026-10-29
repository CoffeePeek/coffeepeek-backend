using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Domain.Entities;

namespace CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;

public sealed partial class CheckIn
{
    public static CheckIn CreateHome(Guid userId, string username, string? note, DateTime preparedAtUtc,
        DateOnly preparedOn, string? brewMethodCode, Guid? coffeeProductId, string? beanNameSnapshot,
        decimal? doseGrams, int? brewDurationSeconds, int? tasteScore, Guid? id = null,
        CheckInVisibility visibility = CheckInVisibility.Private)
    {
        ValidateVisibility(visibility);
        if (userId == Guid.Empty) throw new DomainException("UserId is required.");
        if (id == Guid.Empty) throw new DomainException("CheckInId is required.");
        ValidateHome(note, preparedAtUtc, preparedOn, brewMethodCode, coffeeProductId,
            beanNameSnapshot, doseGrams, brewDurationSeconds, tasteScore);

        var checkIn = new CheckIn
        {
            Id = id ?? Guid.NewGuid(),
            UserId = userId,
            Username = username,
            Kind = CheckInKind.Home,
            ShopId = null,
            VisitedAt = preparedAtUtc,
            PreparedOn = preparedOn,
            Note = note?.Trim() ?? "",
            Visibility = visibility,
            CreatedAtUtc = DateTime.UtcNow
        };
        checkIn.AssignHomeDetails(brewMethodCode, coffeeProductId, beanNameSnapshot,
            doseGrams, brewDurationSeconds, tasteScore);
        checkIn.ResetModeration();
        return checkIn;
    }

    public void UpdateHome(string? note, DateTime preparedAtUtc, DateOnly preparedOn,
        string? brewMethodCode, Guid? coffeeProductId, string? beanNameSnapshot,
        decimal? doseGrams, int? brewDurationSeconds, int? tasteScore, CheckInVisibility? visibility = null)
    {
        EnsureNotDeleted();
        if (visibility.HasValue) ValidateVisibility(visibility.Value);
        if (Kind != CheckInKind.Home) throw new DomainException("Only home check-ins can use the home update operation.");
        ValidateHome(note, preparedAtUtc, preparedOn, brewMethodCode, coffeeProductId,
            beanNameSnapshot, doseGrams, brewDurationSeconds, tasteScore);
        if (visibility.HasValue) Visibility = visibility.Value;
        Note = note?.Trim() ?? "";
        VisitedAt = preparedAtUtc;
        PreparedOn = preparedOn;
        AssignHomeDetails(brewMethodCode, coffeeProductId, beanNameSnapshot,
            doseGrams, brewDurationSeconds, tasteScore);
        ContentRevision++;
        ResetModeration();
        Touch();
    }

    /// <summary>Where the bean came from; set after <see cref="CreateHome"/> or <see cref="UpdateHome"/>.</summary>
    public void SetBeanSource(Guid? shelfBagId, string? roasterNameSnapshot)
    {
        if (Kind != CheckInKind.Home) throw new DomainException("Only home check-ins have a bean source.");
        if (shelfBagId == Guid.Empty) throw new DomainException("ShelfBagId must not be empty.");
        if (ShelfBagPhotoId != null && shelfBagId != ShelfBagId)
            throw new DomainException("Detach the bag card before changing the bag.");
        var roaster = string.IsNullOrWhiteSpace(roasterNameSnapshot) ? null : roasterNameSnapshot.Trim();
        if (roaster?.Length > 250) throw new DomainException("Roaster name must contain at most 250 characters.");
        ShelfBagId = shelfBagId;
        RoasterNameSnapshot = roaster;
    }

    /// <summary>Sort index after every attached photo, so moderation snapshots keep distinct indexes.</summary>
    public int NextPhotoSortIndex() => _shopPhotos.Count == 0 ? 0 : _shopPhotos.Max(p => p.SortIndex) + 1;

    /// <summary>Shows a copy of the linked bag's card with this entry; the copy belongs to the entry, not the bag.</summary>
    public void AttachShelfBagPhoto(CheckInPhoto photo)
    {
        EnsureNotDeleted();
        ArgumentNullException.ThrowIfNull(photo);
        if (Kind != CheckInKind.Home || ShelfBagId == null || ShelfBagPhotoId != null || photo.OwnerId != UserId
            || photo.CanonicalCheckInId != Guid.Empty && photo.CanonicalCheckInId != Id
            || _shopPhotos.Any(p => p.Id == photo.Id || p.StorageKey == photo.StorageKey))
            throw new DomainException("A bag card requires a linked bag and its own photo.");
        photo.AttachTo(Id);
        _shopPhotos.Add(photo);
        ShelfBagPhotoId = photo.Id;
        ContentRevision++;
        ResetModeration();
        Touch();
    }

    /// <summary>Stops showing the bag card; returns the removed photo's ID, or null when none was shown.</summary>
    public Guid? DetachShelfBagPhoto()
    {
        EnsureNotDeleted();
        if (ShelfBagPhotoId is not { } photoId) return null;
        _shopPhotos.RemoveAll(p => p.Id == photoId);
        ShelfBagPhotoId = null;
        ContentRevision++;
        ResetModeration();
        Touch();
        return photoId;
    }

    private void AssignHomeDetails(string? brewMethodCode, Guid? coffeeProductId,
        string? beanNameSnapshot, decimal? doseGrams, int? brewDurationSeconds, int? tasteScore)
    {
        BrewMethodCode = brewMethodCode;
        CoffeeProductId = coffeeProductId;
        BeanNameSnapshot = string.IsNullOrWhiteSpace(beanNameSnapshot) ? null : beanNameSnapshot.Trim();
        DoseGrams = doseGrams;
        BrewDurationSeconds = brewDurationSeconds;
        TasteScore = tasteScore;
    }

    private static void ValidateHome(string? note, DateTime preparedAtUtc, DateOnly preparedOn,
        string? brewMethodCode, Guid? coffeeProductId, string? beanNameSnapshot,
        decimal? doseGrams, int? brewDurationSeconds, int? tasteScore)
    {
        ValidateContent(note, null);
        if (preparedAtUtc == default || preparedAtUtc.Kind != DateTimeKind.Utc
            || preparedAtUtc > DateTime.UtcNow.AddMinutes(BusinessConstants.MaxVisitedAtClockSkewMinutes))
            throw new DomainException("PreparedAt must be a valid UTC time and cannot be in the future.");
        if (preparedOn == default || Math.Abs(preparedOn.DayNumber - DateOnly.FromDateTime(preparedAtUtc).DayNumber) > 1)
            throw new DomainException("PreparedOn must be the local preparation date.");
        if (brewMethodCode is not null && !HomeBrewMethodCode.IsValid(brewMethodCode))
            throw new DomainException("Unknown home brew method.");
        if (coffeeProductId == Guid.Empty) throw new DomainException("CoffeeProductId must not be empty.");
        var beanName = beanNameSnapshot?.Trim();
        if (beanName?.Length > 250 || coffeeProductId.HasValue && string.IsNullOrWhiteSpace(beanName))
            throw new DomainException("Bean name must contain 1–250 characters for a selected product.");
        if (doseGrams.HasValue && (doseGrams <= 0 || doseGrams > 10000 || decimal.Round(doseGrams.Value, 2) != doseGrams.Value))
            throw new DomainException("DoseGrams must be between 0.01 and 10000 with at most two decimal places.");
        if (brewDurationSeconds.HasValue && brewDurationSeconds is < 1 or > 604800)
            throw new DomainException("BrewDurationSeconds must be between 1 and 604800.");
        if (tasteScore.HasValue && tasteScore is < 1 or > 5)
            throw new DomainException("TasteScore must be between 1 and 5.");
    }
}
