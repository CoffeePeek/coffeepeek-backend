using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.CheckIn;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate;

namespace CoffeePeek.Shops.Application.Features.Review.Reports;

public static class GetReviewReportHandler
{
    public static async Task<Response<ReviewReportDetail>> Handle(GetReviewReportQuery query,
        IReviewReportRepository reports, IQueryCheckInRepository checkIns, CancellationToken ct)
    {
        var report = await reports.GetByIdAsync(query.ReportId, ct)
            ?? throw new NotFoundException("Complaint not found.");
        var checkIn = report.CheckInId is { } id ? await checkIns.GetTrackedByIdAsync(id, ct) : null;
        var target = checkIn is null ? null
            : new ReportedCheckInDto(checkIn.Id, checkIn.ShopId, checkIn.UserId,
            checkIn.Username, checkIn.Note, checkIn.Rating?.Place, checkIn.Rating?.Service, checkIn.Rating?.Coffee,
            checkIn.VisitedAt, checkIn.DeletedAtUtc, checkIn.IsPubliclyVisible);
        return Response<ReviewReportDetail>.Success(new(ReviewReportDto.From(report), target));
    }
}
