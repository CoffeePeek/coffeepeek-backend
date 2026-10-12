namespace CoffeePeek.Shops.Application.PublicAddresses;

public sealed record BackfillPublicAddressesCommand(PublicAddressKind Kind, Guid? AfterId, int Limit, bool DryRun, Guid ActorId);
