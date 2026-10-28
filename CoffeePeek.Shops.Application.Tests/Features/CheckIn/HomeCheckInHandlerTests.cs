using System;
using CoffeePeek.Shops.Application.PublicAddresses;
using CoffeePeek.Contract.Dtos.PublicAddresses;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Contract.Dtos.CheckIns;
using CoffeePeek.Contract.Events.Shops;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Drinks;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shops.Application.Features.CheckIn;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using Moq;
using Wolverine;
using DomainCheckIn = CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate.CheckIn;

namespace CoffeePeek.Shops.Application.Tests.Features.CheckIn;

public class HomeCheckInHandlerTests
{
    private readonly Guid _owner = Guid.NewGuid();
    private readonly Mock<IQueryCheckInRepository> _repository = new();
    private readonly Mock<IDrinkCatalog> _drinks = new();
    private readonly Mock<IPublicAddressService> _addresses = new();
    private readonly Mock<IUploadedPhotoLookup> _photos = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IMessageBus> _bus = new();
    private static readonly DateTime PreparedUtc = new(2026, 9, 20, 19, 15, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("cold-brew")]
    [InlineData("chemex")]
    [InlineData("hario-v60")]
    [InlineData("cezve-turkish-coffee")]
    public async Task CreateAcceptsMethodsFromTheShopCatalog(string slug)
    {
        _addresses.Setup(a => a.ResolveAsync(PublicAddressKind.BrewMethod, slug, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PublicAddressMetadata(Guid.NewGuid(), slug, "/brew-methods/" + slug, 1));
        var result = await Create(new CreateHomeCheckInRequest
        {
            PreparedAt = "2026-09-20T22:15:00Z", BrewMethodCode = slug,
            DrinkSlug = "other", CustomDrinkName = "My coffee"
        });
        Assert.Equal(slug, result.Data.BrewMethodCode);
        _repository.Verify(r => r.Add(It.IsAny<DomainCheckIn>()), Times.Once);
    }

    [Fact]
    public async Task UnknownCatalogMethodStopsBeforeSavingOrClaimingPhotos()
    {
        await Assert.ThrowsAsync<ValidationException>(() => Create(new CreateHomeCheckInRequest
        {
            PreparedAt = "2026-09-20T22:15:00Z", BrewMethodCode = "nonexistent-method"
        }));
        _repository.Verify(r => r.Add(It.IsAny<DomainCheckIn>()), Times.Never);
        _unitOfWork.VerifyNoOtherCalls();
        _photos.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task EditResolvesCatalogAliasesButCanKeepARemovedHistoricalMethod()
    {
        var ct = TestContext.Current.CancellationToken;
        var row = DomainCheckIn.CreateHome(_owner, "author", "original", PreparedUtc,
            new DateOnly(2026, 9, 20), "removed-method", null, null, null, null, null);
        _repository.Setup(r => r.GetTrackedByIdAsync(row.Id, ct)).ReturnsAsync(row);
        var request = new UpdateHomeCheckInRequest
        {
            PreparedAt = "2026-09-20T19:15:00Z", BrewMethodCode = "removed-method", Text = "edited"
        };
        var kept = await UpdateHomeCheckInHandler.Handle(new(row.Id, _owner, request),
            _repository.Object, _drinks.Object, _addresses.Object, _unitOfWork.Object, _bus.Object, ct);
        Assert.Equal("removed-method", kept.Data.BrewMethodCode);
        _addresses.VerifyNoOtherCalls();
        await Assert.ThrowsAsync<ValidationException>(() => UpdateHomeCheckInHandler.Handle(
            new(row.Id, _owner, request with { BrewMethodCode = "nonexistent-method", Text = "invalid" }),
            _repository.Object, _drinks.Object, _addresses.Object, _unitOfWork.Object, _bus.Object, ct));
        Assert.Equal("edited", row.Note);
        Assert.Equal("removed-method", row.BrewMethodCode);
        _addresses.Setup(a => a.ResolveAsync(PublicAddressKind.BrewMethod, "old-name", ct))
            .ReturnsAsync(new PublicAddressMetadata(Guid.NewGuid(), "cold-brew", "/brew-methods/cold-brew", 2, true));
        var changed = await UpdateHomeCheckInHandler.Handle(new(row.Id, _owner, request with { BrewMethodCode = "old-name" }),
            _repository.Object, _drinks.Object, _addresses.Object, _unitOfWork.Object, _bus.Object, ct);
        Assert.Equal("cold-brew", changed.Data.BrewMethodCode);
    }
    private Task<CoffeePeek.Shared.Kernel.Response.Response<HomeCheckInDto>> Create(CreateHomeCheckInRequest request) =>
        CreateHomeCheckInHandler.Handle(new(request, _owner, "author"), _repository.Object,
            _drinks.Object, _addresses.Object, _photos.Object, _unitOfWork.Object, _bus.Object,
            TestContext.Current.CancellationToken);

    [Fact]
    public async Task PublicCreateEmitsHomeSnapshotAndReturnsActualPendingVisibility()
    {
        var result = await Create(new CreateHomeCheckInRequest
        {
            PreparedAt = "2026-09-20T22:15:00+03:00", CustomBeanName = "My beans",
            Visibility = CoffeePeek.Contract.Enums.CheckInVisibility.Public
        });
        Assert.Equal(CoffeePeek.Contract.Enums.CheckInVisibility.Public, result.Data.Visibility);
        Assert.Equal(CoffeePeek.Contract.Enums.CheckInModerationState.Pending, result.Data.ModerationState);
        _bus.Verify(b => b.PublishAsync(It.Is<CheckInModerationRequestedEvent>(e =>
            e.ShopId == null && e.Home != null && e.Home.BeanName == "My beans"
            && e.ContentRevision == result.Data.ContentRevision)), Times.Once);
    }

    [Fact]
    public async Task UpdatePreservesOmittedVisibilityAndExplicitHideInvalidatesApproval()
    {
        var ct = TestContext.Current.CancellationToken;
        var row = DomainCheckIn.CreateHome(_owner, "author", "note", PreparedUtc,
            new DateOnly(2026, 9, 20), null, null, null, null, null, null,
            visibility: CheckInVisibility.Public);
        row.ApplyModerationDecision(row.ContentRevision, Guid.NewGuid(), 1,
            CheckInModerationState.Approved, null, DateTime.UtcNow);
        _repository.Setup(r => r.GetTrackedByIdAsync(row.Id, ct)).ReturnsAsync(row);
        var request = new UpdateHomeCheckInRequest { PreparedAt = "2026-09-20T22:15:00+03:00", Text = "edited" };
        var result = await UpdateHomeCheckInHandler.Handle(new(row.Id, _owner, request),
            _repository.Object, _drinks.Object, _addresses.Object, _unitOfWork.Object, _bus.Object, ct);
        Assert.Equal(CoffeePeek.Contract.Enums.CheckInVisibility.Public, result.Data.Visibility);
        Assert.Equal(CoffeePeek.Contract.Enums.CheckInModerationState.Pending, result.Data.ModerationState);
        _bus.Verify(b => b.PublishAsync(It.IsAny<CheckInModerationRequestedEvent>()), Times.Once);
        var hidden = await UpdateHomeCheckInHandler.Handle(new(row.Id, _owner,
            request with { Visibility = CoffeePeek.Contract.Enums.CheckInVisibility.Private }),
            _repository.Object, _drinks.Object, _addresses.Object, _unitOfWork.Object, _bus.Object, ct);
        Assert.Equal(CoffeePeek.Contract.Enums.CheckInVisibility.Private, hidden.Data.Visibility);
        Assert.Equal(CoffeePeek.Contract.Enums.CheckInModerationState.NotSubmitted, hidden.Data.ModerationState);
        Assert.False(row.IsPubliclyVisible);
        _bus.Verify(b => b.PublishAsync(It.IsAny<CheckInModerationRequestedEvent>()), Times.Once);
    }

    [Fact]
    public async Task QuickCreateNeedsOnlyTimestampAndDoesNotFabricateDoseScoreOrBean()
    {
        DomainCheckIn? saved = null;
        _repository.Setup(r => r.Add(It.IsAny<DomainCheckIn>())).Callback<DomainCheckIn>(row => saved = row);
        var result = await Create(new CreateHomeCheckInRequest
        {
            PreparedAt = "2026-09-20T22:15:00+03:00"
        });

        Assert.True(result.IsSuccess);
        Assert.Equal(PreparedUtc, result.Data.PreparedAtUtc);
        Assert.Equal(new DateOnly(2026, 9, 20), result.Data.PreparedOn);
        Assert.Null(result.Data.CoffeeProductId);
        Assert.Null(result.Data.BeanName);
        Assert.Null(result.Data.BrewMethodCode);
        Assert.Null(result.Data.DoseGrams);
        Assert.Null(result.Data.BrewDurationSeconds);
        Assert.Null(result.Data.TasteScore);
        Assert.Equal(CheckInKind.Home, saved!.Kind);
        Assert.Null(saved.ShopId);
        _repository.Verify(r => r.GetPublishedCoffeeProductBySlugAsync(It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
        _repository.Verify(r => r.Add(It.IsAny<DomainCheckIn>()), Times.Once);
        _photos.VerifyNoOtherCalls();
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _bus.Verify(b => b.PublishAsync(It.IsAny<CheckinCreatedEvent>()), Times.Never);
    }

    [Fact]
    public async Task PublishedProductSlugResolvesToStableIdentityAndNameSnapshot()
    {
        var productId = Guid.NewGuid();
        _repository.Setup(r => r.GetPublishedCoffeeProductBySlugAsync("honey-bloom",
            It.IsAny<CancellationToken>())).ReturnsAsync((productId, "Honey Bloom", "Sibaristica"));

        var result = await Create(new CreateHomeCheckInRequest
        {
            PreparedAt = "2026-09-20T22:15:00Z",
            CoffeeProductSlug = " HONEY-BLOOM ",
            BrewMethodCode = "batch_drip",
            DoseGrams = 75m,
            BrewDurationSeconds = 360,
            TasteScore = 4
        });

        Assert.True(result.IsSuccess);
        Assert.Equal(productId, result.Data.CoffeeProductId);
        Assert.Equal("Honey Bloom", result.Data.BeanName);
        Assert.Equal("Sibaristica", result.Data.RoasterName);
        Assert.Null(result.Data.ShelfBagId);
        Assert.Equal(75m, result.Data.DoseGrams);
        Assert.Equal(360, result.Data.BrewDurationSeconds);
        Assert.Equal(4, result.Data.TasteScore);
        _repository.Verify(r => r.GetPublishedCoffeeProductBySlugAsync("honey-bloom",
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("2026-09-20T22:15:00")]
    [InlineData("2026-09-20")]
    [InlineData(null)]
    public async Task TimestampWithoutExplicitOffsetIsRejectedBeforeSaving(string? timestamp)
    {
        await Assert.ThrowsAsync<ValidationException>(() => Create(new CreateHomeCheckInRequest
        {
            PreparedAt = timestamp
        }));
        _repository.Verify(r => r.Add(It.IsAny<DomainCheckIn>()), Times.Never);
        _unitOfWork.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task BeanSelectorsAreExclusiveAndUnpublishedSlugIsNotSelectable()
    {
        await Assert.ThrowsAsync<ValidationException>(() => Create(new CreateHomeCheckInRequest
        {
            PreparedAt = "2026-09-20T22:15:00Z",
            CoffeeProductSlug = "honey-bloom",
            CustomBeanName = "My beans"
        }));
        await Assert.ThrowsAsync<NotFoundException>(() => Create(new CreateHomeCheckInRequest
        {
            PreparedAt = "2026-09-20T22:15:00Z",
            CoffeeProductSlug = "archived-product"
        }));
        _repository.Verify(r => r.Add(It.IsAny<DomainCheckIn>()), Times.Never);
        _unitOfWork.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ArchivedProductCanBeKeptOnEditWithoutRewritingHistoricalNameOrPhotos()
    {
        var ct = TestContext.Current.CancellationToken;
        var productId = Guid.NewGuid();
        var row = DomainCheckIn.CreateHome(_owner, "author", "original", PreparedUtc,
            new DateOnly(2026, 9, 20), "espresso", productId, "Original bean name", 18m, 30, 5);
        row.AddPhotos([new CheckInPhoto("cup.jpg", "image/jpeg", "private/cup.jpg", 100, _owner)]);
        _repository.Setup(r => r.GetTrackedByIdAsync(row.Id, ct)).ReturnsAsync(row);

        await Assert.ThrowsAsync<ValidationException>(() => UpdateHomeCheckInHandler.Handle(
            new UpdateHomeCheckInCommand(row.Id, _owner, new UpdateHomeCheckInRequest
            {
                PreparedAt = "2026-09-20T19:15:00Z",
                CoffeeProductId = Guid.NewGuid()
            }), _repository.Object, _drinks.Object, _addresses.Object, _unitOfWork.Object, _bus.Object, ct));

        var result = await UpdateHomeCheckInHandler.Handle(new UpdateHomeCheckInCommand(row.Id, _owner,
            new UpdateHomeCheckInRequest
            {
                PreparedAt = "2026-09-20T19:15:00Z",
                CoffeeProductId = productId,
                Text = "new note"
            }), _repository.Object, _drinks.Object, _addresses.Object, _unitOfWork.Object, _bus.Object, ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(productId, result.Data.CoffeeProductId);
        Assert.Equal("Original bean name", result.Data.BeanName);
        Assert.Equal("new note", result.Data.Text);
        Assert.Null(result.Data.DoseGrams);
        Assert.Null(result.Data.TasteScore);
        Assert.Single(result.Data.Photos);
        _repository.Verify(r => r.GetPublishedCoffeeProductBySlugAsync(It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(ct), Times.Once);
    }

    [Fact]
    public async Task AnotherUserCannotUpdateOrDeleteOwnersPrivateHomeEntry()
    {
        var ct = TestContext.Current.CancellationToken;
        var row = DomainCheckIn.CreateHome(_owner, "author", null, PreparedUtc,
            new DateOnly(2026, 9, 20), null, null, null, null, null, null);
        _repository.Setup(r => r.GetTrackedByIdAsync(row.Id, ct)).ReturnsAsync(row);
        var stranger = Guid.NewGuid();

        await Assert.ThrowsAsync<NotFoundException>(() => UpdateHomeCheckInHandler.Handle(
            new UpdateHomeCheckInCommand(row.Id, stranger, new UpdateHomeCheckInRequest
            {
                PreparedAt = "2026-09-20T19:15:00Z"
            }), _repository.Object, _drinks.Object, _addresses.Object, _unitOfWork.Object, _bus.Object, ct));
        await Assert.ThrowsAsync<NotFoundException>(() => UpdateHomeCheckInHandler.Handle(
            new DeleteHomeCheckInCommand(row.Id, stranger),
            _repository.Object, _unitOfWork.Object, _bus.Object, ct));
        Assert.Null(row.DeletedAtUtc);
        _unitOfWork.VerifyNoOtherCalls();
        _bus.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DeletingHomeEntryPublishesPhotoTombstone()
    {
        var ct = TestContext.Current.CancellationToken;
        var row = DomainCheckIn.CreateHome(_owner, "author", null, PreparedUtc,
            new DateOnly(2026, 9, 20), null, null, null, null, null, null);
        _repository.Setup(r => r.GetTrackedByIdAsync(row.Id, ct)).ReturnsAsync(row);
        await UpdateHomeCheckInHandler.Handle(new DeleteHomeCheckInCommand(row.Id, _owner),
            _repository.Object, _unitOfWork.Object, _bus.Object, ct);
        _bus.Verify(b => b.PublishAsync(It.Is<CoffeePeek.Contract.Events.CheckInPhotosDeletedEvent>(message =>
            message.CheckInId == row.Id && message.UserId == _owner && message.DeletedAtUtc == row.DeletedAtUtc)), Times.Once);
    }

    [Fact]
    public async Task OwnShelfBagSuppliesTheBeanAndRoasterWithoutACatalogLookup()
    {
        var bagId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        _repository.Setup(r => r.GetOwnedShelfBagBeanAsync(_owner, bagId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((productId, "Honey Bloom", "Sibaristica"));

        var result = await Create(new CreateHomeCheckInRequest
        {
            PreparedAt = "2026-09-20T22:15:00Z", ShelfBagId = bagId, Visibility = CoffeePeek.Contract.Enums.CheckInVisibility.Public
        });

        Assert.Equal(bagId, result.Data.ShelfBagId);
        Assert.Equal(productId, result.Data.CoffeeProductId);
        Assert.Equal("Honey Bloom", result.Data.BeanName);
        Assert.Equal("Sibaristica", result.Data.RoasterName);
        _repository.Verify(r => r.GetPublishedCoffeeProductBySlugAsync(It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
        _bus.Verify(b => b.PublishAsync(It.Is<CheckInModerationRequestedEvent>(e =>
            e.Home!.RoasterName == "Sibaristica" && e.Home.BeanName == "Honey Bloom")), Times.Once);
    }

    [Fact]
    public async Task CustomBagKeepsItsOwnNameAndAnotherOwnersBagIsNotFound()
    {
        var bagId = Guid.NewGuid();
        _repository.Setup(r => r.GetOwnedShelfBagBeanAsync(_owner, bagId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(((Guid?)null, "Gift from Kenya", (string?)null));
        var custom = await Create(new CreateHomeCheckInRequest { PreparedAt = "2026-09-20T22:15:00Z", ShelfBagId = bagId });
        Assert.Null(custom.Data.CoffeeProductId);
        Assert.Equal("Gift from Kenya", custom.Data.BeanName);
        Assert.Null(custom.Data.RoasterName);

        await Assert.ThrowsAsync<NotFoundException>(() => Create(new CreateHomeCheckInRequest
        {
            PreparedAt = "2026-09-20T22:15:00Z", ShelfBagId = Guid.NewGuid()
        }));
        _repository.Verify(r => r.Add(It.IsAny<DomainCheckIn>()), Times.Once);
    }

    [Theory]
    [InlineData("honey-bloom", null)]
    [InlineData(null, "My beans")]
    public async Task ShelfBagCannotBeCombinedWithAnotherBeanSelector(string? slug, string? customName)
    {
        await Assert.ThrowsAsync<ValidationException>(() => Create(new CreateHomeCheckInRequest
        {
            PreparedAt = "2026-09-20T22:15:00Z", ShelfBagId = Guid.NewGuid(),
            CoffeeProductSlug = slug, CustomBeanName = customName
        }));
        _repository.Verify(r => r.GetOwnedShelfBagBeanAsync(It.IsAny<Guid>(), It.IsAny<Guid>(),
            It.IsAny<CancellationToken>()), Times.Never);
        _repository.Verify(r => r.Add(It.IsAny<DomainCheckIn>()), Times.Never);
    }

    [Fact]
    public async Task EditKeepsTheBagsSnapshotsEvenAfterTheBagIsGoneAndClearsTheLinkWithoutIt()
    {
        var ct = TestContext.Current.CancellationToken;
        var bagId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var row = DomainCheckIn.CreateHome(_owner, "author", "", PreparedUtc,
            new DateOnly(2026, 9, 20), null, productId, "Honey Bloom", null, null, null);
        row.SetBeanSource(bagId, "Sibaristica");
        _repository.Setup(r => r.GetTrackedByIdAsync(row.Id, ct)).ReturnsAsync(row);
        var request = new UpdateHomeCheckInRequest { PreparedAt = "2026-09-20T19:15:00Z", ShelfBagId = bagId };

        var kept = await UpdateHomeCheckInHandler.Handle(new(row.Id, _owner, request),
            _repository.Object, _drinks.Object, _addresses.Object, _unitOfWork.Object, _bus.Object, ct);
        Assert.Equal(bagId, kept.Data.ShelfBagId);
        Assert.Equal("Sibaristica", kept.Data.RoasterName);
        _repository.Verify(r => r.GetOwnedShelfBagBeanAsync(It.IsAny<Guid>(), It.IsAny<Guid>(),
            It.IsAny<CancellationToken>()), Times.Never);

        var unlinked = await UpdateHomeCheckInHandler.Handle(new(row.Id, _owner,
                request with { ShelfBagId = null, CoffeeProductId = productId }),
            _repository.Object, _drinks.Object, _addresses.Object, _unitOfWork.Object, _bus.Object, ct);
        Assert.Null(unlinked.Data.ShelfBagId);
        Assert.Equal(productId, unlinked.Data.CoffeeProductId);
        Assert.Equal("Sibaristica", unlinked.Data.RoasterName);
    }
}
