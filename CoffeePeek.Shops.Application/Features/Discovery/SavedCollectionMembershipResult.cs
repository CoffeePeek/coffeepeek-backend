using CoffeePeek.Contract.Dtos.Discovery;

namespace CoffeePeek.Shops.Application.Features.Discovery;

public sealed record SavedCollectionMembershipResult(SavedCollectionSummary[] Collections, bool IsFavorite, bool MembershipChanged);
