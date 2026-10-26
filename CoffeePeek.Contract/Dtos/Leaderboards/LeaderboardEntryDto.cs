using CoffeePeek.Contract.Dtos.PublicAddresses;

namespace CoffeePeek.Contract.Dtos.Leaderboards;

public sealed record LeaderboardEntryDto(int Rank, Guid AuthorId, string Username, string? AvatarUrl,
    PublicAddressDto? Address, int Points, int Additions, int Edits, int CheckIns);
