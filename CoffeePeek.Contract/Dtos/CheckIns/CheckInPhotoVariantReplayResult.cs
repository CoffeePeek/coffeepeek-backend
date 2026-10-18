#nullable enable
namespace CoffeePeek.Contract.Dtos.CheckIns;

public sealed record CheckInPhotoVariantReplayResult(int Published, Guid? NextCursor);
