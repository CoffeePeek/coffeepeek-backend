using CoffeePeek.Shops.Application.Abstractions;

namespace CoffeePeek.Shops.Application.Features.CheckIn;

/// <summary>Bounded Account facts used only by variant delivery, never by original or moderation reads.</summary>
public interface ICheckInPhotoVisibilityCache
{
    Task<CheckInPhotoVisibilityFacts> GetAsync(Guid? viewerId, IAuthorVisibilityLookup source, CancellationToken ct);
}
