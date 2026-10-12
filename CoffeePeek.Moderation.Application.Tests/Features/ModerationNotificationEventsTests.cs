using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Enums;
using CoffeePeek.Contract.Events.Moderation;
using CoffeePeek.Moderation.Application.Features.ShopChangeRequests;
using CoffeePeek.Moderation.Application.Features.ShopIssueReports.CreateShopIssueReport;
using CoffeePeek.Moderation.Domain.Aggregates.ShopChangeRequestAggregate;
using CoffeePeek.Moderation.Domain.Aggregates.ShopIssueReportAggregate;
using CoffeePeek.Shared.Kernel;
using FluentAssertions;
using Moq;

namespace CoffeePeek.Moderation.Application.Tests.Features;

public class ModerationNotificationEventsTests
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task ShopChange_CreatesNotificationForSavedRequest()
    {
        var repository = new Mock<IShopChangeRequestRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        var shopId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var command = new CreateShopChangeRequestCommand(shopId, CoffeePeek.Contract.Enums.ShopChangeSection.Description,
            new ShopChangePayloadDto(Description: "Updated")) { UserId = userId };

        var (response, notification) = await CreateShopChangeRequestHandler.Handle(command,
            repository.Object, unitOfWork.Object, _ct);

        response.IsSuccess.Should().BeTrue();
        notification.Should().Be(new ModerationSubmissionCreated(ModerationSubmissionKind.ShopChange,
            response.Data!.Id, userId, "Description", shopId));
        unitOfWork.Verify(x => x.SaveChangesAsync(_ct), Times.Once);
    }

    [Fact]
    public async Task ShopIssue_CreatesNotificationForSavedReport()
    {
        var repository = new Mock<IShopIssueReportRepository>();
        var unitOfWork = new Mock<IUnitOfWork>();
        var shopId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var command = new CreateShopIssueReportCommand(shopId, ShopIssueCategory.IncorrectAddress,
            "The location is wrong") { UserId = userId };

        var (response, notification) = await CreateShopIssueReportHandler.Handle(command,
            repository.Object, unitOfWork.Object, _ct);

        response.IsSuccess.Should().BeTrue();
        notification.Kind.Should().Be(ModerationSubmissionKind.ShopIssue);
        notification.UserId.Should().Be(userId);
        notification.RelatedId.Should().Be(shopId);
        notification.Details.Should().Be(command.Description);
        unitOfWork.Verify(x => x.SaveChangesAsync(_ct), Times.Once);
    }
}
