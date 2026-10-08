namespace CoffeePeek.Account.Application.Features.Admin.Stats;

public interface IAdminStatsClient
{
    Task<AdminPlatformStatsSnapshot> GetPlatformStatsAsync(CancellationToken cancellationToken = default);
}
