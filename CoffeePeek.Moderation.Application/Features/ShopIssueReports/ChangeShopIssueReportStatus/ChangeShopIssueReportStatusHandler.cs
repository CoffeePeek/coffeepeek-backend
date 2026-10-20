using CoffeePeek.Contract.Enums;
using CoffeePeek.Contract.Events.Moderation;
using Wolverine;
using CoffeePeek.Moderation.Domain.Aggregates.ShopIssueReportAggregate;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Response;

namespace CoffeePeek.Moderation.Application.Features.ShopIssueReports.ChangeShopIssueReportStatus;

public static class ChangeShopIssueReportStatusHandler
{
    public static async Task<UpdateEntityResponse<ShopIssueReportStatus>> Handle(
        ChangeShopIssueReportStatusCommand command,
        IShopIssueReportRepository repository,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct)
    {
        var report = await repository.GetById(command.ReportId, ct);

        if (report == null)
            throw new NotFoundException("Shop issue report not found");

        var oldStatus = (ShopIssueReportStatus)report.Status;

        switch (command.Status)
        {
            case ShopIssueReportStatus.Reviewed:
                report.MarkReviewed(command.UserId);
                break;
            case ShopIssueReportStatus.Fixed:
                report.MarkFixed(command.UserId);
                break;
            case ShopIssueReportStatus.Invalid:
                report.MarkInvalid(command.UserId);
                break;
            default:
                throw new ValidationException($"Cannot transition a shop issue report to status '{command.Status}'.");
        }

        await unitOfWork.SaveChangesAsync(ct);

        if (oldStatus != (ShopIssueReportStatus)report.Status)
            await bus.PublishAsync(new ModerationDecisionChanged(Guid.NewGuid(), report.ReportedByUserId,
                ModerationSubmissionKind.ShopIssue, report.Id, report.Status.ToString()));

        return UpdateEntityResponse<ShopIssueReportStatus>.Success(
            (ShopIssueReportStatus)report.Status, oldEntity: oldStatus);
    }
}
