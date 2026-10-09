using CoffeePeek.Contract.Dtos.Coffee;
using CoffeePeek.Shared.Kernel;

namespace CoffeePeek.Shops.Application.Features.CoffeeCatalog;

public static class CoffeeCatalogHandler
{
    // Writer factories hide their DbContext; IUnitOfWork selects the shared Wolverine transaction.
    public static Task<AdminCoffeeDto> Handle(CreateCoffeeCommand command, ICoffeeProductWriter store, IUnitOfWork unitOfWork, CancellationToken ct)
        => store.CreateAsync(command.Request, ct);
    public static Task<AdminCoffeeDto?> Handle(EditCoffeeCommand command, ICoffeeProductWriter store, IUnitOfWork unitOfWork, CancellationToken ct)
        => store.EditAsync(command.Id, command.Request, ct);
    public static Task<CoffeeImportResult> Handle(ImportCoffeeSnapshotCommand command, ICoffeeImportWriter store, CancellationToken ct)
        => store.ImportAsync(command.Request, ct);
    public static Task Handle(RecordCoffeeImportFailureCommand command, ICoffeeImportWriter store, IUnitOfWork unitOfWork, CancellationToken ct)
        => store.RecordFailureAsync(command.Failure, ct);
    public static Task<RoasterTagDto> Handle(CreateRoasterTagCommand command, ICoffeeCatalogTagWriter store, IUnitOfWork unitOfWork, CancellationToken ct)
        => store.CreateTagAsync(command.Request, ct);
    public static Task<RoasterTagDto?> Handle(EditRoasterTagCommand command, ICoffeeCatalogTagWriter store, IUnitOfWork unitOfWork, CancellationToken ct)
        => store.EditTagAsync(command.Id, command.Request, ct);
    public static Task Handle(SetRoasterTagsCommand command, ICoffeeCatalogTagWriter store, IUnitOfWork unitOfWork, CancellationToken ct)
        => store.SetRoasterTagsAsync(command.RoasterId, command.TagIds, command.ActorId, ct);
    public static Task<bool> Handle(ArchiveCoffeeCommand command, ICoffeeProductWriter store, IUnitOfWork unitOfWork, CancellationToken ct)
        => store.ArchiveAsync(command.Id, ct);
}
