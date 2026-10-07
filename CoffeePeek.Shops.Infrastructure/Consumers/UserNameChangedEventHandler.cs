using CoffeePeek.Contract.Events.Account;
using CoffeePeek.Shops.Persistance.Configuration;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.Shops.Infrastructure.Consumers;

public class UserNameChangedHandler(ShopsDbContext dbContext)
{
    public async Task Handle(UserNameChangedEvent message, CancellationToken cancellationToken = default)
    {
        await dbContext.CheckIns.Where(c => c.UserId == message.UserId && c.Username != message.NewUserName)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Username, message.NewUserName)
                .SetProperty(c => c.RowVersion, Guid.NewGuid()), cancellationToken);
    }
}
