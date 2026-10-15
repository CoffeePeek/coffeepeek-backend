using CoffeePeek.Contract.Dtos.PublicAddresses;

namespace CoffeePeek.Contract.Dtos.Profiles;

public sealed record ProfileFollowUserDto(string UserName, string? AvatarUrl, PublicAddressDto Address);
