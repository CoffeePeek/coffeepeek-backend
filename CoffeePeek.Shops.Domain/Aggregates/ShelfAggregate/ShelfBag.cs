using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Shops.Domain.Aggregates.ShelfAggregate;

/// <summary>One bag of coffee on a user's private shelf. Positions are maintained by <see cref="ShelfLineup"/>.</summary>
public sealed class ShelfBag
{
    public const int MaxTextLength = 250;
    public const int MaxStorageKeyLength = 255;
    public const int MaxContentTypeLength = 100;
    public const int MinWeightGrams = 1;
    public const int MaxWeightGrams = 10000;

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public ShelfBagStatus Status { get; private set; }
    public int? Position { get; private set; }
    public Guid? PhotoId { get; private set; }
    public string? PhotoStorageKey { get; private set; }
    public string? PhotoContentType { get; private set; }
    public Guid? CoffeeProductId { get; private set; }
    public string? Name { get; private set; }
    public string? RoasterName { get; private set; }
    public int? WeightGrams { get; private set; }
    public ShelfBagGrind? Grind { get; private set; }
    public DateOnly? RoastedOn { get; private set; }
    public DateTime AddedAtUtc { get; private set; }
    public DateTime? FinishedAtUtc { get; private set; }
    public int? Rating { get; private set; }
    public bool? BuyAgain { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    private ShelfBag() { }

    /// <summary>Creates an unplaced bag; <see cref="ShelfLineup.AddToFront"/> gives it a position.</summary>
    public static ShelfBag Create(Guid id, Guid userId, ShelfBagDetails details, ShelfBagPhoto? photo, DateTime nowUtc)
    {
        if (id == Guid.Empty || userId == Guid.Empty) throw new DomainException("Invalid shelf bag identity.");
        var bag = new ShelfBag { Id = id, UserId = userId, Status = ShelfBagStatus.OnShelf, AddedAtUtc = nowUtc, UpdatedAtUtc = nowUtc };
        bag.ApplyPhoto(photo);
        bag.ApplyDetails(details, nowUtc);
        return bag;
    }

    /// <summary>Replaces all details; a non-null photo replaces the card. Returns the released card, if any.</summary>
    public Guid? UpdateDetails(ShelfBagDetails details, ShelfBagPhoto? newPhoto, DateTime nowUtc)
    {
        Guid? released = null;
        if (newPhoto != null && newPhoto.Id != PhotoId)
        {
            released = PhotoId;
            ApplyPhoto(newPhoto);
        }
        ApplyDetails(details, nowUtc);
        UpdatedAtUtc = nowUtc;
        return released;
    }

    public bool IsOwnedBy(Guid userId) => UserId == userId;

    internal void MoveTo(int position)
    {
        if (Status != ShelfBagStatus.OnShelf || position < 0) throw new DomainException("Invalid shelf position.");
        Position = position;
    }

    public static void ValidateRating(int? rating)
    {
        if (rating is < 1 or > 5) throw new DomainException("Rating must be between 1 and 5.");
    }

    internal void MarkFinished(int? rating, bool? buyAgain, DateTime nowUtc)
    {
        ValidateRating(rating);
        Status = ShelfBagStatus.Finished;
        Position = null;
        FinishedAtUtc = nowUtc;
        Rating = rating;
        BuyAgain = buyAgain;
        UpdatedAtUtc = nowUtc;
    }

    internal void MarkRestored(DateTime nowUtc)
    {
        Status = ShelfBagStatus.OnShelf;
        FinishedAtUtc = null;
        Rating = null;
        BuyAgain = null;
        UpdatedAtUtc = nowUtc;
    }

    private void ApplyPhoto(ShelfBagPhoto? photo)
    {
        if (photo == null) return;
        if (photo.Id == Guid.Empty || string.IsNullOrWhiteSpace(photo.StorageKey) || photo.StorageKey.Length > MaxStorageKeyLength
            || string.IsNullOrWhiteSpace(photo.ContentType) || photo.ContentType.Length > MaxContentTypeLength)
            throw new DomainException("Invalid shelf bag photo.");
        PhotoId = photo.Id;
        PhotoStorageKey = photo.StorageKey;
        PhotoContentType = photo.ContentType;
    }

    /// <summary>Checks details before any side effect, such as claiming a card, is attempted.</summary>
    public static void EnsureValid(ShelfBagDetails details, bool hasPhoto, DateTime nowUtc)
    {
        var name = Normalize(details.Name, "Name");
        Normalize(details.RoasterName, "RoasterName");
        if (details.CoffeeProductId == Guid.Empty || (details.CoffeeProductId != null && name == null))
            throw new DomainException("A catalog product requires its name snapshot.");
        if (details.WeightGrams is < MinWeightGrams or > MaxWeightGrams)
            throw new DomainException($"WeightGrams must be between {MinWeightGrams} and {MaxWeightGrams}.");
        if (details.Grind is { } grind && !Enum.IsDefined(grind))
            throw new DomainException("Invalid grind.");
        if (details.RoastedOn > DateOnly.FromDateTime(nowUtc).AddDays(1))
            throw new DomainException("RoastedOn cannot be more than one day in the future.");
        if (!hasPhoto && name == null)
            throw new DomainException("A shelf bag needs a photo or a bean.");
    }

    private void ApplyDetails(ShelfBagDetails details, DateTime nowUtc)
    {
        EnsureValid(details, PhotoId != null, nowUtc);
        CoffeeProductId = details.CoffeeProductId;
        Name = Normalize(details.Name, "Name");
        RoasterName = Normalize(details.RoasterName, "RoasterName");
        WeightGrams = details.WeightGrams;
        Grind = details.Grind;
        RoastedOn = details.RoastedOn;
    }

    private static string? Normalize(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        if (value.Length > MaxTextLength) throw new DomainException($"{field} must be at most {MaxTextLength} characters.");
        return value;
    }
}
