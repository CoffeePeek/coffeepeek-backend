using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Enums;
using CoffeePeek.Contract.Events.Moderation;
using CoffeePeek.Moderation.Application.Abstractions;
using CoffeePeek.Moderation.Application.Features.Review.ChangeStatusModerationReview;
using CoffeePeek.Moderation.Application.Features.Shop.UpdateModerationShopStatus;
using CoffeePeek.Moderation.Application.Features.Shop.UpdateShop;
using CoffeePeek.Moderation.Domain.Aggregates;
using CoffeePeek.Moderation.Domain.Aggregates.ModerationReviewAggregate;
using CoffeePeek.Moderation.Domain.Entities;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using FluentAssertions;
using MapsterMapper;
using Moq;
using Wolverine;

namespace CoffeePeek.Moderation.Application.Tests.Features;

public class ModerationStatusHandlersTests
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;
    private readonly Mock<IModerationAuditLogRepository> _audit = new();
    private readonly Mock<IMapper> _mapper = new();
    private readonly Mock<IUnitOfWork> _uow = new();

    public ModerationStatusHandlersTests()
    {
        _mapper.Setup(m => m.Map<ShopDto>(It.IsAny<object>())).Returns(new ShopDto { Name = "Test Cafe" });
        _mapper.Setup(m => m.Map<ModerationShopDto>(It.IsAny<object>())).Returns(new ModerationShopDto());
    }

    private static ModerationShop ApprovableShop()
    {
        var shop = ModerationShop.Create("Test Cafe", Guid.NewGuid(), Guid.NewGuid(), null);
        shop.SetLocation(new ModerationLocation("Validated street", 53.9m, 27.5m));
        return shop;
    }

    private Task<(CoffeePeek.Shared.Kernel.Response.Response, object?)> ChangeShopStatus(
        ModerationShop shop, ModerationStatus status, string? comment = null)
    {
        var repo = new Mock<IModerationShopRepository>();
        repo.Setup(r => r.GetByIdAsync(shop.Id, It.IsAny<CancellationToken>())).ReturnsAsync(shop);
        return UpdateModerationCoffeeShopStatusHandler.Handle(
            new UpdateModerationCoffeeShopStatusCommand(Guid.NewGuid(), shop.Id, status, comment),
            repo.Object, _audit.Object, _mapper.Object, _ct);
    }

    [Fact]
    public async Task ShopApprove_EmitsApprovedEventOnce()
    {
        var shop = ApprovableShop();

        var (_, first) = await ChangeShopStatus(shop, ModerationStatus.Approved);
        var (_, second) = await ChangeShopStatus(shop, ModerationStatus.Approved);

        first.Should().BeOfType<ModerationShopApprovedEvent>();
        second.Should().BeNull("re-approve must not publish the shop again");
    }

    [Fact]
    public async Task ShopReject_AfterApprove_IsRefused()
    {
        var shop = ApprovableShop();
        await ChangeShopStatus(shop, ModerationStatus.Approved);

        var act = () => ChangeShopStatus(shop, ModerationStatus.Rejected, "Changed mind");

        await act.Should().ThrowAsync<DomainException>();
    }

    [Theory]
    [InlineData(ModerationStatus.Rejected)]
    [InlineData(ModerationStatus.Pending)]
    public async Task CheckInUnapprove_EmitsCanonicalDecision(ModerationStatus target)
    {
        var review = ModerationReview.CreateCheckInSnapshot(Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(),
            "alice", "Great coffee and service", 5, 5, 5, null, DateTime.UtcNow, []);
        review.Approve(Guid.NewGuid());
        var repo = new Mock<IModerationReviewRepository>();
        repo.Setup(r => r.GetById(review.Id, It.IsAny<CancellationToken>())).ReturnsAsync(review);

        var (_, outbound) = await ChangeStatusModerationReviewHandler.Handle(
            new ChangeStatusModerationReviewCommand(Guid.NewGuid(), review.Id, target, "Abusive content", null),
            repo.Object, _audit.Object, _uow.Object, _ct);

        outbound.Should().BeOfType<CheckInModerationDecidedEvent>();
        outbound!.CheckInId.Should().Be(review.CheckInId);
        outbound.Status.Should().Be(target);
    }

    [Fact]
    public async Task UpdateShop_LoadsFullAggregate_AndRefusesApprovedShop()
    {
        var shop = ApprovableShop();
        shop.Approve();
        var repo = new Mock<IModerationShopRepository>();
        repo.Setup(r => r.GetByIdAsync(shop.Id, It.IsAny<CancellationToken>())).ReturnsAsync(shop);

        var response = await UpdateModerationCoffeeShopHandler.Handle(
            new UpdateModerationCoffeeShopCommand(new ModerationShopDto { Id = shop.Id, Name = "Renamed" }, Guid.NewGuid(), true),
            repo.Object, Mock.Of<IYandexGeocodingService>(), _mapper.Object, new OutgoingMessages(), _uow.Object, _ct);

        response.IsSuccess.Should().BeFalse();
        shop.Name.Should().Be("Test Cafe");
        repo.Verify(r => r.GetByIdWithOutDetails(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
