using CoffeePeek.Shared.Domain.Entities;
using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;

public sealed class MenuOcrJob : Entity<Guid>
{
    public const string ProviderOutcomeUnknownError =
        "OCR_PROVIDER_OUTCOME_UNKNOWN: worker stopped before storing a result; provider may have charged. Retry requires an explicit new request.";

    public MenuOcrSourceKind SourceKind { get; private set; }
    public Guid SourceId { get; private set; }
    public Guid? PublishedShopId { get; private set; }
    public Guid? ShopMenuId { get; private set; }
    public long? MenuRevision { get; private set; }
    public string PhotoSetHash { get; private set; } = null!;
    public string InputPhotosJson { get; private set; } = null!;
    public MenuOcrJobStatus Status { get; private set; }
    public string? ResultJson { get; private set; }
    public string? Error { get; private set; }
    public DateTime RequestedAtUtc { get; private set; }
    public DateTime? StartedAtUtc { get; private set; }
    public DateTime? ResultStoredAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }

    private MenuOcrJob() { }

    public static MenuOcrJob CreatePublished(
        ShopMenu menu,
        IReadOnlyCollection<ShopMenuPhoto> inputPhotos,
        DateTime requestedAtUtc)
    {
        if (inputPhotos.Count == 0)
            throw new DomainException("At least one menu photo is required for OCR.");
        if (inputPhotos.Any(input => !menu.Photos.Any(current =>
                current.StorageKey == input.StorageKey &&
                current.FileName == input.FileName &&
                current.ContentType == input.ContentType &&
                current.SizeBytes == input.SizeBytes &&
                current.MediaPhotoId == input.MediaPhotoId)))
            throw new DomainException("OCR input photo is not in the current menu.");

        return new MenuOcrJob
        {
            Id = Guid.NewGuid(),
            SourceKind = MenuOcrSourceKind.PublishedShop,
            SourceId = menu.CoffeeShopId,
            PublishedShopId = menu.CoffeeShopId,
            ShopMenuId = menu.Id,
            MenuRevision = menu.InputRevision,
            PhotoSetHash = MenuOcrPhotoSet.Hash(menu.Photos),
            InputPhotosJson = MenuOcrPhotoSet.Serialize(inputPhotos),
            Status = MenuOcrJobStatus.Pending,
            RequestedAtUtc = requestedAtUtc
        };
    }

    public bool Matches(ShopMenu menu) =>
        SourceKind == MenuOcrSourceKind.PublishedShop &&
        ShopMenuId == menu.Id &&
        MenuRevision == menu.InputRevision &&
        menu.ActiveOcrJobId == Id &&
        PhotoSetHash == MenuOcrPhotoSet.Hash(menu.Photos);

    public void Start(DateTime nowUtc)
    {
        Require(MenuOcrJobStatus.Pending);
        Status = MenuOcrJobStatus.Running;
        StartedAtUtc = nowUtc;
    }

    public void StoreResult(string resultJson, DateTime nowUtc)
    {
        Require(MenuOcrJobStatus.Running);
        if (string.IsNullOrWhiteSpace(resultJson))
            throw new DomainException("OCR result is required.");
        ResultJson = resultJson;
        ResultStoredAtUtc = nowUtc;
        Status = MenuOcrJobStatus.ResultStored;
    }

    public void MarkApplied(DateTime nowUtc)
    {
        Require(MenuOcrJobStatus.ResultStored);
        Status = MenuOcrJobStatus.Applied;
        CompletedAtUtc = nowUtc;
    }

    public void MarkStale(DateTime nowUtc)
    {
        if (Status is not (MenuOcrJobStatus.Pending or MenuOcrJobStatus.Running or MenuOcrJobStatus.ResultStored))
            throw new DomainException($"Cannot mark OCR job {Status} as stale.");
        Status = MenuOcrJobStatus.Stale;
        CompletedAtUtc = nowUtc;
    }

    public void MarkFailed(string error, DateTime nowUtc)
    {
        if (Status is not (MenuOcrJobStatus.Pending or MenuOcrJobStatus.Running or MenuOcrJobStatus.ResultStored))
            throw new DomainException($"Cannot fail OCR job {Status}.");
        if (string.IsNullOrWhiteSpace(error))
            throw new DomainException("OCR failure reason is required.");
        Error = error.Length > BusinessConstants.MaxMenuParseErrorLength
            ? error[..BusinessConstants.MaxMenuParseErrorLength]
            : error;
        Status = MenuOcrJobStatus.Failed;
        CompletedAtUtc = nowUtc;
    }

    public void MarkProviderOutcomeUnknown(DateTime nowUtc)
    {
        Require(MenuOcrJobStatus.Running);
        MarkFailed(ProviderOutcomeUnknownError, nowUtc);
    }

    private void Require(MenuOcrJobStatus expected)
    {
        if (Status != expected)
            throw new DomainException($"OCR job must be {expected}, but is {Status}.");
    }
}
