using CoffeePeek.Moderation.Domain.Common.Enums;
using CoffeePeek.Shared.Domain.Entities;

namespace CoffeePeek.Moderation.Domain.Aggregates.ShopIssueReportAggregate;

public partial class ShopIssueReport : Entity<Guid>
{
    public Guid ShopId { get; private set; }
    public Guid ReportedByUserId { get; private set; }
    public ShopIssueCategory Category { get; private set; }
    public string? Description { get; private set; }
    public ShopIssueReportStatus Status { get; private set; }
    public Guid? ReviewedBy { get; private set; }
    public DateTime? ReviewedAt { get; private set; }

    // ReSharper disable once UnusedMember.Local
    private ShopIssueReport()
    {
    }

    internal ShopIssueReport(Guid reportedByUserId, Guid shopId, ShopIssueCategory category, string? description)
    {
        Id = Guid.NewGuid();
        ReportedByUserId = reportedByUserId;
        ShopId = shopId;
        Category = category;
        Description = description;
        Status = ShopIssueReportStatus.Submitted;
        CreatedAtUtc = DateTime.UtcNow;
    }
}
