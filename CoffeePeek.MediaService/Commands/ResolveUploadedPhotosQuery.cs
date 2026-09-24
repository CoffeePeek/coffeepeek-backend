using System.Text.Json.Serialization;

namespace CoffeePeek.MediaService.Commands;

public record ResolveUploadedPhotosQuery(IReadOnlyCollection<string> StorageKeys)
{
    /// <summary>Always taken from the authenticated principal, never from the request body.</summary>
    [JsonIgnore] public Guid OwnerId { get; init; }
}
