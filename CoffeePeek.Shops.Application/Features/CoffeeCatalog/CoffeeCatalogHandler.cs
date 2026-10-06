using CoffeePeek.Contract.Dtos.Coffee;

namespace CoffeePeek.Shops.Application.Features.CoffeeCatalog;

public static class CoffeeCatalogHandler
{
    public static Task<AdminCoffeeDto> Handle(CreateCoffeeCommand command, ICoffeeCatalogStore store, CancellationToken ct)
        => store.CreateAsync(command.Request, ct);
    public static Task<AdminCoffeeDto?> Handle(EditCoffeeCommand command, ICoffeeCatalogStore store, CancellationToken ct)
        => store.EditAsync(command.Id, command.Request, ct);
    public static Task<CoffeeImportResult> Handle(ImportCoffeeSnapshotCommand command, ICoffeeCatalogStore store, CancellationToken ct)
        => store.ImportAsync(command.Request, ct);
    public static Task<RoasterTagDto> Handle(CreateRoasterTagCommand command, ICoffeeCatalogStore store, CancellationToken ct)
        => store.CreateTagAsync(command.Request, ct);
    public static Task<RoasterTagDto?> Handle(EditRoasterTagCommand command, ICoffeeCatalogStore store, CancellationToken ct)
        => store.EditTagAsync(command.Id, command.Request, ct);
    public static Task Handle(SetRoasterTagsCommand command, ICoffeeCatalogStore store, CancellationToken ct)
        => store.SetRoasterTagsAsync(command.RoasterId, command.TagIds, command.ActorId, ct);
    public static Task<bool> Handle(ArchiveCoffeeCommand command, ICoffeeCatalogStore store, CancellationToken ct)
        => store.ArchiveAsync(command.Id, ct);
}
