using CoffeePeek.Moderation.Domain.Aggregates;
using CoffeeShop.Moderation.Persistence.Configuration;
using Microsoft.EntityFrameworkCore;

namespace CoffeeShop.Moderation.Persistence.Repositories;

public class ModerationRoasterRepository(ModerationDbContext context) : IModerationRoasterRepository
{
    private DbSet<ModerationRoaster> Repository => context.ModerationRoasters;

    public Task<ModerationRoaster?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Repository
            .Include(r => r.Contact)
            .Include(r => r.Location)
            .Include(r => r.Photos)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task AddAsync(ModerationRoaster roaster)
    {
        ArgumentNullException.ThrowIfNull(roaster);
        await Repository.AddAsync(roaster);
    }
}
