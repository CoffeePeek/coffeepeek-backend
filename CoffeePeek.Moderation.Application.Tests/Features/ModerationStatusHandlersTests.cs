using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Enums;
using CoffeePeek.Contract.Events.Moderation;
using CoffeePeek.Moderation.Application.Features.Geocoding;
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
    private readonly Mock<IMessageBus> _bus = new();

    [Fact]
    public async Task LeaderboardOverrideRequiresTrustedAdminAndWritesReasonToAuditEvenWithoutStatusChange()
    {
        var review = ModerationReview.CreateCheckInSnapshot(Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(),
            "user", "", null, null, null, null, DateTime.UtcNow, []);
        var repository = new Mock<IModerationReviewRepository>();
        repository.Setup(x => x.GetById(review.Id, _ct)).ReturnsAsync(review);
        var command = new ChangeStatusModerationReviewCommand(Guid.NewGuid(), review.Id, ModerationStatus.Approved,
            null, null, CountsForLeaderboard: true, LeaderboardReason: "GPS error confirmed");
        await Assert.ThrowsAsync<DomainException>(() => ChangeStatusModerationReviewHandler.Handle(command,
            repository.Object, _audit.Object, _uow.Object, _ct));
        var (_, decision) = await ChangeStatusModerationReviewHandler.Handle(command with { CanOverrideLeaderboard = true },
            repository.Object, _audit.Object, _uow.Object, _ct);
        Assert.True(decision!.LeaderboardOverride);
        Assert.Equal(ModerationStatus.Approved, decision.Status);
        Assert.Equal(2, decision.DecisionSequence);
        Assert.Equal("GPS error confirmed", review.LeaderboardReason);
        _uow.Verify(x => x.SaveChangesAsync(_ct), Times.Once);
        Assert.Single(_audit.Invocations);
    }

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
            repo.Object, _audit.Object, _mapper.Object, _bus.Object, _ct);
    }

    [Fact]
    public async Task ShopApprove_EmitsApprovedEventOnce()
    {
        var shop = ApprovableShop();

        var (_, first) = await ChangeShopStatus(shop, ModerationStatus.Approved);
        var (_, second) = await ChangeShopStatus(shop, ModerationStatus.Approved);

        first.Should().BeOfType<ModerationShopApprovedEvent>();
        second.Should().BeNull("re-approve must not publish the shop again");
        _bus.Verify(x => x.PublishAsync(It.Is<ModerationDecisionChanged>(e => e.UserId == shop.UserId
            && e.SubmissionId == shop.Id && e.Kind == ModerationSubmissionKind.Shop && e.Status == "Approved")), Times.Once);
    }

    [Fact]
    public async Task ShopReject_RepeatedDecision_WritesAuditOnce()
    {
        var shop = ApprovableShop();
        await ChangeShopStatus(shop, ModerationStatus.Rejected, "Duplicate shop");
        await ChangeShopStatus(shop, ModerationStatus.Rejected, " Duplicate shop ");

        _audit.Verify(x => x.AddAsync(It.IsAny<ModerationAuditLog>(), It.IsAny<CancellationToken>()), Times.Once);
        _bus.Verify(x => x.PublishAsync(It.Is<ModerationDecisionChanged>(e => e.UserId == shop.UserId
            && e.Status == "Rejected")), Times.Once);
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
            repo.Object, _mapper.Object, _bus.Object, _uow.Object, _ct);

        response.IsSuccess.Should().BeFalse();
        shop.Name.Should().Be("Test Cafe");
        repo.Verify(r => r.GetByIdWithOutDetails(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateRejectedShop_QueuesGeocodingForNewAddress()
    {
        var shop = ApprovableShop();
        shop.Reject("Wrong address");
        var repo = new Mock<IModerationShopRepository>();
        repo.Setup(r => r.GetByIdAsync(shop.Id, _ct)).ReturnsAsync(shop);

        var response = await UpdateModerationCoffeeShopHandler.Handle(
            new(new ModerationShopDto { Id = shop.Id, Name = shop.Name, CityId = shop.CityId, Address = "New address" },
                Guid.NewGuid(), true), repo.Object, _mapper.Object, _bus.Object, _uow.Object, _ct);

        response.IsSuccess.Should().BeTrue();
        shop.Location!.IsAddressValidated.Should().BeFalse();
        _bus.Verify(bus => bus.PublishAsync(It.Is<ModerationGeocodingRequested>(request =>
            request.Kind == ModerationGeocodingKind.Shop && request.EntityId == shop.Id &&
            request.SubmittedAddress == "New address")), Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SavingSameAddress_QueuesOnlyWhenStillUnvalidated(bool validated)
    {
        var shop = ApprovableShop();
        if (!validated) shop.SetLocation(new ModerationLocation("Validated street"));
        var repo = new Mock<IModerationShopRepository>();
        repo.Setup(r => r.GetByIdAsync(shop.Id, _ct)).ReturnsAsync(shop);

        var response = await UpdateModerationCoffeeShopHandler.Handle(
            new(new ModerationShopDto
            {
                Id = shop.Id, Name = shop.Name, CityId = shop.CityId, Address = "Validated street"
            }, Guid.NewGuid(), true), repo.Object, _mapper.Object, _bus.Object, _uow.Object, _ct);

        response.IsSuccess.Should().BeTrue();
        _bus.Verify(b => b.PublishAsync(It.IsAny<ModerationGeocodingRequested>()),
            validated ? Times.Never() : Times.Once());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Validated street")]
    public async Task ChangedCity_WithSameStreet_ResetsCoordinatesAndQueuesGeocoding(string? address)
    {
        var shop = ApprovableShop();
        var newCityId = Guid.NewGuid();
        var repo = new Mock<IModerationShopRepository>();
        repo.Setup(r => r.GetByIdAsync(shop.Id, _ct)).ReturnsAsync(shop);

        var response = await UpdateModerationCoffeeShopHandler.Handle(
            new(new ModerationShopDto { Id = shop.Id, Name = shop.Name, CityId = newCityId, Address = address },
                Guid.NewGuid(), true), repo.Object, _mapper.Object, _bus.Object, _uow.Object, _ct);

        response.IsSuccess.Should().BeTrue();
        shop.CityId.Should().Be(newCityId);
        shop.Location!.IsAddressValidated.Should().BeFalse();
        shop.Location.Latitude.Should().BeNull();
        shop.Location.Longitude.Should().BeNull();
        shop.Location.Address.Should().Be("Validated street");
        _bus.Verify(b => b.PublishAsync(It.Is<ModerationGeocodingRequested>(r =>
            r.EntityId == shop.Id && r.SubmittedAddress == "Validated street")), Times.Once);
    }
}
