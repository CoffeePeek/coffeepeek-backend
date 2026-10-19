namespace CoffeePeek.Shops.Application.Abstractions;

public interface IMenuPhotoDownloader
{
    Task<IReadOnlyList<MenuVisionPhoto>> DownloadAsync(
        IReadOnlyList<MenuPhotoDownloadRequest> photos,
        CancellationToken ct = default);
}
