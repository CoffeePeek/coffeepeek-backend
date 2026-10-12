using CoffeePeek.Moderation.Application.Abstractions;
using CoffeePeek.Moderation.Domain.Aggregates.ModerationReviewAggregate;
using CoffeePeek.Moderation.Domain.Common.Enums;

namespace CoffeePeek.ModerationService.Tests;

internal sealed class StubModerationReviewRepository(ModerationReview? review = null) : IQueryModerationReviewRepository
{
    public Task<ModerationReview?> GetById(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(review?.Id == id ? review : null);

    public Task<(ModerationReview[] Items, int TotalCount)> GetPagedAsync(int page, int pageSize,
        ModerationStatus? status, string? search, Guid? userId = null,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
