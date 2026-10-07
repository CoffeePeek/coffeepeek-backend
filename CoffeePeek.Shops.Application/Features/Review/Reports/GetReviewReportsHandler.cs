using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.CheckIn;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate;

namespace CoffeePeek.Shops.Application.Features.Review.Reports;

public static class GetReviewReportsHandler
{
    public static async Task<Response<ReviewReportPage>> Handle(GetReviewReportsQuery query,
        IReviewReportRepository reports, CancellationToken ct)
    {
        if (query.Status.HasValue && !Enum.IsDefined(query.Status.Value))
            throw new ValidationException("Unknown complaint status.");
        if (query.Page < 1 || query.PageSize is < 1 or > 100 || (long)(query.Page - 1) * query.PageSize > int.MaxValue)
            throw new ValidationException("Page must be positive and pageSize must be between 1 and 100.");
        var (items, total) = await reports.GetPageAsync(query.Status, query.Page, query.PageSize, ct);
        return Response<ReviewReportPage>.Success(new(items.Select(ReviewReportDto.From).ToArray(),
            total, query.Page, query.PageSize));
    }
}
