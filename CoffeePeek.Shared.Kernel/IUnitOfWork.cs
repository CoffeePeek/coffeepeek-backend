namespace CoffeePeek.Shared.Kernel;

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    Task ExecuteAsync(Func<Task> operation, CancellationToken ct = default);
    void ClearTracking();
}