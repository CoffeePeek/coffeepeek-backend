using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;
using CoffeePeek.Shops.Application.Features.Public.Stats;
using CoffeePeek.Shops.Domain.Aggregates.ReviewAggregate;

namespace CoffeePeek.Shops.Application.Features.Review.Reports;

public static class SubmitReviewReportHandler
{
    public static async Task<Response<ReviewReportDto>> Handle(SubmitReviewReportCommand command,
        IReviewReportRepository reports, IReviewRepository reviews, IUnitOfWork uow, CancellationToken ct)
    {
        var report = ReviewReport.Create(command.ReviewId, command.UserId, command.Text);
        var review = await reviews.GetById(command.ReviewId, ct);
        if (review == null || review.IsSoftDelete)
            throw new NotFoundException("Published review not found.");
        reports.Add(report);
        await uow.SaveChangesAsync(ct);
        return Response<ReviewReportDto>.Success(ReviewReportDto.From(report));
    }
}

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

public static class GetReviewReportHandler
{
    public static async Task<Response<ReviewReportDetail>> Handle(GetReviewReportQuery query,
        IReviewReportRepository reports, IReviewRepository reviews, CancellationToken ct)
    {
        var report = await reports.GetByIdAsync(query.ReportId, ct)
            ?? throw new NotFoundException("Complaint not found.");
        var review = await reviews.GetById(report.ReviewId, ct)
            ?? throw new NotFoundException("Review not found.");
        return Response<ReviewReportDetail>.Success(new(ReviewReportDto.From(report),
            new(review.Id, review.CoffeeShopId, review.UserId, review.UserName, review.Header, review.Comment,
                review.Rating.Place, review.Rating.Service, review.Rating.Coffee, review.IsSoftDelete, review.IsRemovedByAdmin)));
    }
}

public static class ResolveReviewReportHandler
{
    public static async Task<Response<ReviewReportDto>> Handle(ResolveReviewReportCommand command,
        IReviewReportRepository reports, IReviewRepository reviews, IUnitOfWork uow,
        ICacheService cache, CancellationToken ct)
    {
        var report = await reports.GetByIdAsync(command.ReportId, ct)
            ?? throw new NotFoundException("Complaint not found.");
        var changed = report.Resolve(command.DeleteReview, command.AdminId);
        var review = command.DeleteReview ? await reviews.GetById(report.ReviewId, ct)
            ?? throw new NotFoundException("Review not found.") : null;
        if (changed)
        {
            review?.RemoveByAdmin();
            await uow.SaveChangesAsync(ct);
        }
        // Also invalidate on identical retries in case the previous request failed after saving.
        if (review != null)
        {
            await cache.RemoveAsync(CacheKey.Shop.Detail(review.CoffeeShopId));
            await PublicStatsCacheInvalidator.InvalidateAsync(cache, ct);
            await cache.RemoveByPattern(CacheKey.Shop.SearchPattern(), ct);
        }
        return Response<ReviewReportDto>.Success(ReviewReportDto.From(report));
    }
}
