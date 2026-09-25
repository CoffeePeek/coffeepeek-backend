using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Moderation.Application.Features.Review.GetAllModerationReviews;
using CoffeePeek.Moderation.Application.Features.Roaster.GetAllModerationRoasters;
using CoffeePeek.Moderation.Application.Features.Shop.GetAllModerationShops;
using CoffeePeek.Moderation.Domain.Aggregates;
using CoffeePeek.Moderation.Domain.Aggregates.Enums;
using CoffeePeek.Moderation.Domain.Aggregates.ModerationReviewAggregate;
using FluentAssertions;
using MapsterMapper;
using Moq;
using ContractModerationStatus = CoffeePeek.Contract.Enums.ModerationStatus;
using DomainModerationStatus = CoffeePeek.Moderation.Domain.Common.Enums.ModerationStatus;

namespace CoffeePeek.Moderation.Application.Tests.Features;

/// <summary>
/// GET */mine reuses the GetAll handlers; these tests prove the submitter id reaches the repository
/// (so a user only sees their own items) and that the default query keeps the unfiltered moderator view.
/// </summary>
public class MyModerationSubmissionsHandlersTests
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;
    private readonly Mock<IMapper> _mapper = new();

    public MyModerationSubmissionsHandlersTests()
    {
        _mapper.Setup(m => m.Map<ModerationShopDto[]>(It.IsAny<object>())).Returns([]);
        _mapper.Setup(m => m.Map<ModerationRoasterDto[]>(It.IsAny<object>())).Returns([]);
        _mapper.Setup(m => m.Map<ModerationReviewDto[]>(It.IsAny<object>())).Returns([]);
    }

    private static Mock<IQueryModerationShopRepository> ShopRepo(int total = 0)
    {
        var repo = new Mock<IQueryModerationShopRepository>();
        repo.Setup(r => r.GetPagedForReviewAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DomainModerationStatus?>(), It.IsAny<string?>(),
                It.IsAny<CoffeeFocus?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((IReadOnlyList<ModerationShop>)[], total));
        return repo;
    }

    [Fact]
    public async Task Shops_UserIdIsForwardedToRepository()
    {
        var userId = Guid.NewGuid();
        var repo = ShopRepo();

        await GetAllModerationShopsHandler.Handle(
            new GetAllModerationShopsQuery(UserId: userId), repo.Object, _mapper.Object, _ct);

        repo.Verify(r => r.GetPagedForReviewAsync(
            1, 20, null, null, null, userId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Shops_DefaultQuery_DoesNotFilterByUser()
    {
        var repo = ShopRepo();

        await GetAllModerationShopsHandler.Handle(new GetAllModerationShopsQuery(), repo.Object, _mapper.Object, _ct);

        repo.Verify(r => r.GetPagedForReviewAsync(
            1, 20, null, null, null, null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Shops_PagingMathUnchanged()
    {
        var repo = ShopRepo(total: 45);

        var response = await GetAllModerationShopsHandler.Handle(
            new GetAllModerationShopsQuery(Page: 2, PageSize: 20, UserId: Guid.NewGuid()),
            repo.Object, _mapper.Object, _ct);

        response.Data!.TotalItems.Should().Be(45);
        response.Data.TotalPages.Should().Be(3);
        response.Data.CurrentPage.Should().Be(2);
        response.Data.PageSize.Should().Be(20);
    }

    [Fact]
    public async Task Roasters_UserIdAndStatusAreForwardedToRepository()
    {
        var userId = Guid.NewGuid();
        var repo = new Mock<IQueryModerationRoasterRepository>();
        repo.Setup(r => r.GetPagedForReviewAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DomainModerationStatus?>(), It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(((IReadOnlyList<ModerationRoaster>)[], 45));

        var response = await GetAllModerationRoastersHandler.Handle(
            new GetAllModerationRoastersQuery(Status: ContractModerationStatus.Rejected, UserId: userId),
            repo.Object, _mapper.Object, _ct);

        repo.Verify(r => r.GetPagedForReviewAsync(
            1, 20, DomainModerationStatus.Rejected, userId, It.IsAny<CancellationToken>()), Times.Once);
        response.Data!.TotalPages.Should().Be(3);
    }

    [Fact]
    public async Task Reviews_UserIdIsForwardedToRepository()
    {
        var userId = Guid.NewGuid();
        var repo = new Mock<IQueryModerationReviewRepository>();
        repo.Setup(r => r.GetPagedAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DomainModerationStatus?>(), It.IsAny<string?>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((ModerationReview[])[], 0));

        await GetAllModerationReviewsHandler.Handle(
            new GetAllModerationReviewsQuery(UserId: userId), repo.Object, _mapper.Object, _ct);

        repo.Verify(r => r.GetPagedAsync(
            1, 20, null, null, userId, It.IsAny<CancellationToken>()), Times.Once);
    }
}
