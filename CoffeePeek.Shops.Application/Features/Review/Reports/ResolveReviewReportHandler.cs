using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.CheckIn;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate;

namespace CoffeePeek.Shops.Application.Features.Review.Reports;

public static class ResolveReviewReportHandler
{
    public static async Task<Response<ReviewReportDto>> Handle(ResolveReviewReportCommand command,
        IReviewReportRepository reports, IQueryCheckInRepository checkIns, IUnitOfWork uow,
        ICacheService cache, CancellationToken ct)
    {
        var report = await reports.GetByIdAsync(command.ReportId, ct)
            ?? throw new NotFoundException("Complaint not found.");
        var changed = report.Resolve(command.DeleteCheckIn, command.AdminId);
        var checkIn = command.DeleteCheckIn && report.CheckInId is { } id
            ? await checkIns.GetTrackedByIdAsync(id, ct) : null;
        if (changed)
        {
            if (checkIn is { IsPubliclyVisible: true })
                checkIn.Delete();
            await uow.SaveChangesAsync(ct);
        }
        // Retry invalidation after a committed decision whose response or cache operation failed.
        if (checkIn is not null)
            await CheckInCacheInvalidator.InvalidateAsync(cache, checkIn.ShopId, checkIn.UserId, ct);
        return Response<ReviewReportDto>.Success(ReviewReportDto.From(report));
    }
}
