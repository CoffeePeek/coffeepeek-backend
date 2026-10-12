using CoffeePeek.Contract.Dtos.PublicAddresses;

namespace CoffeePeek.Shops.Application.PublicAddresses;

public sealed record ChangePublicAddressCommand(PublicAddressKind Kind, Guid Id, ChangePublicAddressRequest Request, Guid ActorId);
