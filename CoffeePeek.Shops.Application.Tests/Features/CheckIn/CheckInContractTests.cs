using System;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Events.Shops;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shops.Application.Features.CheckIn.CreateCheckIn;
using CoffeePeek.Shops.Application.Mapper;
using CoffeePeek.Shops.Application.ValidationStrategy.CheckIn;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using FluentAssertions;
using Moq;
using Wolverine;

namespace CoffeePeek.Shops.Application.Tests.Features.CheckIn;

public class CheckInContractTests
{
    private readonly Mock<IQueryCoffeeShopRepository> _shops = new();
    private readonly Mock<IQueryCheckInRepository> _checkIns = new();

    private static CreateCheckInCommand Command(bool isPublic = false) => new(
        Guid.NewGuid(), isPublic, DateTime.UtcNow.AddDays(-2),
        Note: isPublic ? "Excellent coffee and service" : null,
        Rating: new RatingDto { Coffee = 5, Place = 4, Service = 3 },
        Header: isPublic ? "A separate title" : null)
        { UserId = Guid.NewGuid(), UserName = "visitor" };

    private CheckInValidationStrategy Validator()
    {
        _shops.Setup(s => s.Exists(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _checkIns.Setup(c => c.ExistsSinceAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _checkIns.Setup(c => c.CountSinceAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        return new CheckInValidationStrategy(_shops.Object, _checkIns.Object);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Handle_PreservesRatingPhotosAndExplicitPublicText(bool isPublic)
    {
        var command = Command(isPublic) with
        {
            Photos = [new UploadedPhotoDto("coffee.jpg", "image/jpeg", "checkins/coffee.jpg", 1234)]
        };
        var repo = new Mock<IQueryCheckInRepository>();
        var uow = new Mock<IUnitOfWork>();
        var bus = new Mock<IMessageBus>();
        var cache = new Mock<ICacheService>();
        var mapper = MapsterConfiguration.CreateMapper(new MediaPublicUrlOptions());
        CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate.CheckIn saved = null;
        CheckinCreatedEvent published = null;
        repo.Setup(r => r.Add(It.IsAny<CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate.CheckIn>()))
            .Callback<CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate.CheckIn>(c => saved = c);
        bus.Setup(b => b.PublishAsync(It.IsAny<object>()))
            .Callback<object, DeliveryOptions>((e, _) => published = (CheckinCreatedEvent)e)
            .Returns(ValueTask.CompletedTask);

        var response = await CreateCheckInHandler.Handle(command, repo.Object, uow.Object,
            bus.Object, Validator(), mapper, cache.Object, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        saved.Rating.Coffee.Should().Be(5);
        saved.Rating.Place.Should().Be(4);
        saved.Rating.Service.Should().Be(3);
        saved.VisitedAt.Should().Be(command.VisitedAt);
        saved.ShopPhotos.Should().ContainSingle().Which.StorageKey.Should().Be("checkins/coffee.jpg");
        uow.Verify(u => u.SaveChangesAsync(CancellationToken.None), Times.Once);
        if (isPublic)
        {
            published.Should().NotBeNull();
            published.ReviewDto.Header.Should().Be(command.Header);
            published.ReviewDto.Comment.Should().Be(command.Note);
            published.ReviewDto.Username.Should().Be("visitor");
            published.ReviewDto.CoffeeShopId.Should().Be(command.CoffeeShopId);
            published.ReviewDto.UserId.Should().Be(command.UserId);
            published.ReviewDto.Photos.Should().BeEquivalentTo(command.Photos);
        }
        else
        {
            published.Should().BeNull();
            saved.Note.Should().BeNull();
        }
    }

    [Fact]
    public async Task Private_AllOptionalFieldsAbsent_IsValid()
    {
        (await Validator().ValidateAsync(Command(), CancellationToken.None)).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingRating_IsInvalid(bool isPublic)
    {
        var result = await Validator().ValidateAsync(Command(isPublic) with { Rating = null }, CancellationToken.None);
        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Rating");
    }

    [Theory]
    [InlineData(0, 5, 5)]
    [InlineData(5, 6, 5)]
    [InlineData(5, 5, -1)]
    public async Task InvalidRatings_AreRejectedForBothVisibilities(int coffee, int service, int place)
    {
        foreach (var isPublic in new[] { false, true })
        {
            var command = Command(isPublic) with { Rating = new RatingDto { Coffee = coffee, Service = service, Place = place } };
            (await Validator().ValidateAsync(command, CancellationToken.None)).IsValid.Should().BeFalse();
        }
    }

    [Theory]
    [InlineData("ab", "Long enough description")]
    [InlineData(" a ", "Long enough description")]
    [InlineData("Title", null)]
    [InlineData("Title", "")]
    [InlineData("Title", "         ")]
    [InlineData("Title", "123456789")]
    [InlineData("Title", "  short   ")]
    public async Task PublicShortHeaderOrMissingNote_IsValidationError(string header, string note)
    {
        (await Validator().ValidateAsync(Command(true) with { Header = header, Note = note }, CancellationToken.None))
            .IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task PublicMissingHeader_IsValidWhenDescriptionExists(string header)
    {
        (await Validator().ValidateAsync(Command(true) with { Header = header }, CancellationToken.None))
            .IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0, 10, true)]
    [InlineData(3, 10, true)]
    [InlineData(100, 500, true)]
    [InlineData(2, 10, false)]
    [InlineData(101, 10, false)]
    [InlineData(3, 501, false)]
    public async Task PublicTextLimits_MatchReviewDomain(int headerLength, int noteLength, bool valid)
    {
        var header = headerLength == 0 ? null : new string('h', headerLength);
        var command = Command(true) with { Header = header, Note = new string('n', noteLength) };
        (await Validator().ValidateAsync(command, CancellationToken.None)).IsValid.Should().Be(valid);
    }

    [Fact]
    public async Task RecentCheckIn_IsValidationError()
    {
        var validator = Validator();
        _checkIns.Setup(c => c.ExistsSinceAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await validator.ValidateAsync(Command(), CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("3 hours");
    }

    [Fact]
    public async Task FourthCheckInToday_IsValidationError()
    {
        var validator = Validator();
        _checkIns.Setup(c => c.CountSinceAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);

        var result = await validator.ValidateAsync(Command(), CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("3 per day");
    }

    [Fact]
    public async Task MissingOrFutureDate_IsValidationError()
    {
        foreach (var date in new[] { default(DateTime), DateTime.UtcNow.AddDays(1) })
            (await Validator().ValidateAsync(Command() with { VisitedAt = date }, CancellationToken.None)).IsValid.Should().BeFalse();
    }
}
