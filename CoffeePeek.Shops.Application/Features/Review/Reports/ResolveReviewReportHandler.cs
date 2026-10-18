using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.CheckIn;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate;
using Wolverine;

namespace CoffeePeek.Shops.Application.Features.Review.Reports;

public static class ResolveReviewReportHandler
{
    public static async Task<Response<ReviewReportDto>> Handle(ResolveReviewReportCommand command,
        IReviewReportRepository reports, IQueryCheckInRepository checkIns, IUnitOfWork uow,
        IMessageBus bus, CancellationToken ct)
    {
        var report = await reports.GetByIdAsync(command.ReportId, ct)
            ?? throw new NotFoundException("Complaint not found.");
        var changed = report.Resolve(command.DeleteCheckIn, command.AdminId);
        var checkIn = command.DeleteCheckIn && report.CheckInId is { } id
            ? await checkIns.GetTrackedByIdAsync(id, ct) : null;
        if (changed)
        {
            if (checkIn is not null)
            {
                checkIn.Delete();
                await bus.PublishAsync(new CoffeePeek.Contract.Events.CheckInPhotosDeletedEvent(
                    checkIn.Id, checkIn.UserId, checkIn.DeletedAtUtc!.Value));
            }
            await uow.SaveChangesAsync(ct);
            if (checkIn is not null)
                await bus.PublishAsync(new CheckInCacheInvalidationRequested(
                    checkIn.Id, checkIn.ShopId, checkIn.UserId));
        }
        return Response<ReviewReportDto>.Success(ReviewReportDto.From(report));
    }
}
