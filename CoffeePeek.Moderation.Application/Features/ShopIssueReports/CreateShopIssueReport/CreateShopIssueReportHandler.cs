using CoffeePeek.Moderation.Domain.Aggregates.ShopIssueReportAggregate;
using CoffeePeek.Moderation.Domain.Common.Enums;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Response;

namespace CoffeePeek.Moderation.Application.Features.ShopIssueReports.CreateShopIssueReport;

public static class CreateShopIssueReportHandler
{
    public static async Task<CreateEntityResponse> Handle(
        CreateShopIssueReportCommand command,
        IShopIssueReportRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var report = ShopIssueReport.Create(
            command.UserId,
            command.ShopId,
            (ShopIssueCategory)command.Category,
            command.Description);

        repository.Add(report);

        await unitOfWork.SaveChangesAsync(ct);

        return CreateEntityResponse.Success(entityId: report.Id);
    }
}
