#nullable enable
using System.Text.Json.Serialization;

namespace CoffeePeek.Contract.Dtos.CheckIns;

public record CheckInPhotoDto(Guid Id, string FileName, string ContentType, string StorageKey,
    long SizeBytes, int SortIndex, string Url)
{
    /// <summary>Ready protected variants. Omitted until available or when rollout is disabled.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CheckInPhotoVariantDto[]? Variants { get; init; }
}
