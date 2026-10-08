using System.Text.Json.Serialization;
using CoffeePeek.MediaService.Commands.Base;
using CoffeePeek.MediaService.Configuration;

namespace CoffeePeek.MediaService.Commands;

public record GenerateShopPhotosCommand(
    List<PhotoRequest> Requests,
    [property:JsonIgnore] Guid OwnerId,
    string? StorageKeyPrefix = null,
    BucketType Bucket = BucketType.Shop);
