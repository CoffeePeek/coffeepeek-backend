using CoffeePeek.Moderation.Application.Abstractions;
using CoffeePeek.Contract.Dtos.Admin;
using CoffeePeek.Moderation.Domain.Aggregates.ShopImportCandidateAggregate;
using CoffeePeek.Shared.Kernel.Response;

namespace CoffeePeek.Moderation.Application.Features.Admin.Stats;

public static class GetAdminModerationStatsHandler
{
    public static async Task<Response<AdminServiceStatsDto>> Handle(
        GetAdminModerationStatsQuery _,
        IAdminModerationStatsQueryRepository repository,
        IShopImportCandidateRepository importRepository,
        CancellationToken ct)
    {
        // Sequential: both repositories share the same scoped ModerationDbContext.
        // Parallel Task.WhenAll causes InvalidOperationException (concurrent DbContext use).
        var (pendingShops, pendingCheckIns) = await repository.GetStatsAsync(ct);
        var import = await importRepository.GetStatsAsync(ct);

        return Response<AdminServiceStatsDto>.Success(new AdminServiceStatsDto(
            PendingModerationShops: pendingShops,
            PendingModerationCheckIns: pendingCheckIns,
            ImportPending: import.Pending,
            ImportPublished: import.Published,
            ImportRejected: import.Rejected,
            ImportSkipped: import.Skipped,
            ImportInFeed: import.InFeed));
    }
}
