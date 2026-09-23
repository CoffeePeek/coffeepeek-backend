using CoffeePeek.Moderation.Domain.Common.Enums;
using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Moderation.Domain.Aggregates.ShopIssueReportAggregate;

public partial class ShopIssueReport
{
    public static ShopIssueReport Create(Guid reportedByUserId, Guid shopId, ShopIssueCategory category, string? description)
    {
        if (shopId == Guid.Empty)
            throw new DomainException($"{nameof(shopId)} cannot be empty.");

        if (reportedByUserId == Guid.Empty)
            throw new DomainException($"{nameof(reportedByUserId)} cannot be empty.");

        var normalizedDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();

        if (category == ShopIssueCategory.Other && normalizedDescription is null)
            throw new DomainException("Description is required when category is Other.");

        if (normalizedDescription != null &&
            normalizedDescription.Length is < BusinessConstants.MinShopIssueReportDescriptionLength or > BusinessConstants.MaxShopIssueReportDescriptionLength)
            throw new DomainException(
                $"{nameof(description)} must be between {BusinessConstants.MinShopIssueReportDescriptionLength} and {BusinessConstants.MaxShopIssueReportDescriptionLength} characters.");

        return new ShopIssueReport(reportedByUserId, shopId, category, normalizedDescription);
    }

    public void MarkReviewed(Guid moderatorId) => TransitionTo(ShopIssueReportStatus.Reviewed, moderatorId);

    public void MarkFixed(Guid moderatorId) => TransitionTo(ShopIssueReportStatus.Fixed, moderatorId);

    public void MarkInvalid(Guid moderatorId) => TransitionTo(ShopIssueReportStatus.Invalid, moderatorId);

    private void TransitionTo(ShopIssueReportStatus status, Guid moderatorId)
    {
        if (moderatorId == Guid.Empty)
            throw new DomainException($"{nameof(moderatorId)} cannot be empty.");

        // Fixed and Invalid are terminal; a stale admin tab must not reopen a closed report.
        if (Status is ShopIssueReportStatus.Fixed or ShopIssueReportStatus.Invalid)
            throw new DomainException($"Report is already closed as {Status}.");

        Status = status;
        ReviewedBy = moderatorId;
        ReviewedAt = DateTime.UtcNow;
    }
}
