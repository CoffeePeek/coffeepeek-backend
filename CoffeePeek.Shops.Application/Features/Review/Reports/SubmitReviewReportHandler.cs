using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.CheckIn;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate;

namespace CoffeePeek.Shops.Application.Features.Review.Reports;

public static class SubmitReviewReportHandler
{
    public static async Task<Response<ReviewReportDto>> Handle(SubmitReviewReportCommand command,
        IReviewReportRepository reports, IQueryCheckInRepository checkIns, CancellationToken ct)
    {
        var report = ReviewReport.Create(command.CheckInId, command.UserId, command.Text);
        var checkIn = await checkIns.GetTrackedByIdAsync(command.CheckInId, ct);
        if (checkIn is null || !checkIn.IsPubliclyVisible)
            throw new NotFoundException("Published check-in not found.");
        reports.Add(report);
        return Response<ReviewReportDto>.Success(ReviewReportDto.From(report));
    }
}
