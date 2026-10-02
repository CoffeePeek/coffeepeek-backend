using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Moderation.Application.Features.Review.SendReviewToModeration;
using CoffeePeek.Moderation.Application.Features.Review.UpdateCoffeeShopReview;
using CoffeePeek.Moderation.Domain.Aggregates.ModerationReviewAggregate;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Drinks;
using CoffeePeek.Shared.Validation;
using FluentAssertions;
using MapsterMapper;
using Moq;

namespace CoffeePeek.Moderation.Application.Tests.Features;

public class ReviewDrinkTests
{
    [Fact]
    public async Task Submit_StoresCustomDrink()
    {
        var ct = TestContext.Current.CancellationToken;
        var command = new SendReviewToModerationCommand(Guid.NewGuid(), null, "A sufficiently long review comment",
            new RatingDto { Place = 5, Service = 5, Coffee = 5 }, null)
        { UserId = Guid.NewGuid(), UserName = "visitor", DrinkSlug = "other", CustomDrinkName = "  Эспрессо-тоник  " };
        var repo = new Mock<IModerationReviewRepository>();
        ModerationReview? saved = null;
        repo.Setup(r => r.Add(It.IsAny<ModerationReview>())).Callback<ModerationReview>(r => saved = r);
        var catalog = new Mock<IDrinkCatalog>(MockBehavior.Strict);
        await SendReviewToModerationHandler.Handle(command, new SendReviewToModerationValidationStrategy(),
            Mock.Of<CoffeePeek.Moderation.Domain.Aggregates.IQueryModerationShopRepository>(), repo.Object,
            Mock.Of<IUnitOfWork>(), catalog.Object, ct);
        saved!.DrinkSlug.Should().Be("other");
        saved.CustomDrinkName.Should().Be("Эспрессо-тоник");
        saved.DrinkNameEn.Should().Be("Эспрессо-тоник");
    }

    [Fact]
    public async Task Submit_UsesTrustedCheckInSnapshotWithoutRecheckingDeactivatedCatalog()
    {
        var ct = TestContext.Current.CancellationToken;
        var command = new SendReviewToModerationCommand(Guid.NewGuid(), null, "A sufficiently long review comment",
            new RatingDto { Place = 5, Service = 5, Coffee = 5 }, null)
        {
            UserId = Guid.NewGuid(), UserName = "visitor", DrinkSlug = "latte",
            TrustedDrink = new("latte", null, "Латте", "Latte")
        };
        var repo = new Mock<IModerationReviewRepository>();
        ModerationReview? saved = null;
        repo.Setup(r => r.Add(It.IsAny<ModerationReview>())).Callback<ModerationReview>(r => saved = r);
        await SendReviewToModerationHandler.Handle(command, new SendReviewToModerationValidationStrategy(),
            Mock.Of<CoffeePeek.Moderation.Domain.Aggregates.IQueryModerationShopRepository>(), repo.Object,
            Mock.Of<IUnitOfWork>(), Mock.Of<IDrinkCatalog>(MockBehavior.Strict), ct);
        saved!.DrinkNameRu.Should().Be("Латте");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Edit_PreservesExistingSelectionOrExplicitlyClearsIt(bool sameSelection, bool clear)
    {
        var ct = TestContext.Current.CancellationToken;
        var review = ModerationReview.Create(Guid.NewGuid(), Guid.NewGuid(), null, "visitor", null,
            "A sufficiently long review comment", 5, 5, 5, []);
        review.SetDrink(new("latte", null, "Латте", "Latte"));
        var repo = new Mock<IModerationReviewRepository>();
        repo.Setup(r => r.GetById(review.Id, ct)).ReturnsAsync(review);
        var mapper = new Mock<IMapper>();
        mapper.Setup(m => m.Map<ModerationReviewDto>(review)).Returns(() => new ModerationReviewDto
        { Id = review.Id, DrinkSlug = review.DrinkSlug, DrinkNameRu = review.DrinkNameRu });
        var command = new UpdateCoffeeShopReviewCommand(review.UserId, review.Id, null,
            review.Comment, new RatingDto { Place = 5, Service = 5, Coffee = 5 })
        { DrinkSlug = sameSelection ? "latte" : null, ClearDrink = clear };
        var (response, outgoing) = await UpdateCoffeeShopReviewHandler.Handle(command,
            new ReviewUpdateValidationStrategy(), repo.Object, mapper.Object, Mock.Of<IUnitOfWork>(),
            Mock.Of<IDrinkCatalog>(MockBehavior.Strict), ct);
        response.IsSuccess.Should().BeTrue();
        outgoing!.Review.DrinkSlug.Should().Be(clear ? null : "latte");
        review.DrinkNameRu.Should().Be(clear ? null : "Латте");
    }
}
