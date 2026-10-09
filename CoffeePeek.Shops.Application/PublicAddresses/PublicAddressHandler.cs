using CoffeePeek.Contract.Dtos.PublicAddresses;

namespace CoffeePeek.Shops.Application.PublicAddresses;

public static class PublicAddressHandler
{
    public static Task<PublicAddressMetadata> Handle(ChangePublicAddressCommand command, IPublicAddressService addresses, CancellationToken ct)
        => addresses.ChangeAsync(command.Kind, command.Id, command.Request, command.ActorId, ct);

    public static Task<PublicAddressBackfillResult> Handle(BackfillPublicAddressesCommand command, IPublicAddressService addresses, CancellationToken ct)
        => addresses.BackfillAsync(command.Kind, command.AfterId, command.Limit, command.DryRun, command.ActorId, ct);
}
