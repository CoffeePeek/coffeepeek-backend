using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shared.Persistence.Data;

public class UnitOfWork<TDbContext>(TDbContext context) : IUnitOfWork
    where TDbContext : DbContext
{
    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            return await context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            throw new ConflictException("A database conflict occurred. The record may already exist.", ex);
        }
    }

    public async Task ExecuteAsync(Func<Task> operation, CancellationToken ct = default)
    {
        var strategy = context.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(ct);
            try
            {
                await operation();
                await context.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        });
    }

    public void ClearTracking() => context.ChangeTracker.Clear();
}