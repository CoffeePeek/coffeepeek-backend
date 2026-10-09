using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Dtos.CheckIns;
using CoffeePeek.Contract.Events.Shops;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Drinks;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shops.Application.Features.CheckIn;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using CoffeePeek.Shops.Domain.Entities;
using Moq;
using Wolverine;
using Visibility = CoffeePeek.Contract.Enums.CheckInVisibility;
using DomainCheckIn = CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate.CheckIn;

namespace CoffeePeek.Shops.Application.Tests.Features.CheckIn;

public class UnifiedCheckInHandlerTests
{
    private readonly Mock<IQueryCheckInRepository> _repository = new();
    private readonly Mock<IQueryCoffeeShopRepository> _shops = new();
    private readonly Mock<IDrinkCatalog> _drinks = new();
    private readonly Mock<IUploadedPhotoLookup> _photos = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IMessageBus> _bus = new();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _shopId = Guid.NewGuid();

    public UnifiedCheckInHandlerTests()
    {
        _shops.Setup(s => s.Exists(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _shops.Setup(s => s.GetShopNamesByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string> { [_shopId] = "shop" });
    }

    private CreateUnifiedCheckInRequest Request(Visibility visibility = Visibility.Private) =>
        new(_shopId, "x", new RatingDto { Coffee = 3, Service = 4, Place = 5 }, visibility);

    private Task<CoffeePeek.Shared.Kernel.Response.Response<UnifiedCheckInDto>> Create(CreateUnifiedCheckInRequest request) =>
        CreateUnifiedCheckInHandler.Handle(new(request, _userId, "author"), _repository.Object, _shops.Object,
            _drinks.Object, _photos.Object, _unitOfWork.Object, _bus.Object, CancellationToken.None);

    [Fact]
    public async Task SmallestPrivateRequestUsesNowAndSkipsOptionalLookups()
    {
        var before = DateTime.UtcNow;
        var result = await Create(Request());
        Assert.True(result.IsSuccess);
        Assert.Equal(Visibility.Private, result.Data.Visibility);
        Assert.InRange(result.Data.VisitedAt, before, DateTime.UtcNow);
        Assert.Equal("shop", result.Data.ShopName);
        _photos.VerifyNoOtherCalls();
        _drinks.VerifyNoOtherCalls();
        _bus.Verify(b => b.PublishAsync(It.Is<CheckinCreatedEvent>(e => e.UserId == _userId && e.CheckInId == result.Data.Id)), Times.Once);
        _bus.Verify(b => b.PublishAsync(It.Is<CheckInCacheInvalidationRequested>(e =>
            e.CheckInId == result.Data.Id && e.ShopId == _shopId && e.UserId == _userId)), Times.Once);
        _repository.Verify(r => r.Add(It.Is<DomainCheckIn>(c => c.Id == result.Data.Id)), Times.Once);
    }

    [Fact]
    public async Task PublicCreationPublishesCanonicalIdentityAndRevision()
    {
        var result = await Create(Request(Visibility.Public));
        _bus.Verify(b => b.PublishAsync(It.Is<CheckInModerationRequestedEvent>(e => e.CheckInId == result.Data.Id
            && e.ContentRevision == 1 && e.Text == "x" && e.Rating.Coffee == 3)), Times.Once);
        _bus.Verify(b => b.PublishAsync(It.Is<CheckinCreatedEvent>(e => e.CheckInId == result.Data.Id)), Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreationCooldownAppliesToRequestedShop(bool recentVisitAtSameShop)
    {
        _repository.Setup(r => r.ExistsSinceAsync(_userId, It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _repository.Setup(r => r.ExistsSinceAsync(_userId, _shopId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(recentVisitAtSameShop);

        if (recentVisitAtSameShop)
        {
            var error = await Assert.ThrowsAsync<ValidationException>(() => Create(Request()));
            Assert.Contains("same coffee shop", error.Message);
            _repository.Verify(r => r.Add(It.IsAny<DomainCheckIn>()), Times.Never);
            _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
            _bus.Verify(b => b.PublishAsync(It.IsAny<CheckinCreatedEvent>()), Times.Never);
        }
        else
        {
            Assert.True((await Create(Request())).IsSuccess);
            _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        _repository.Verify(r => r.ExistsSinceAsync(_userId, _shopId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SixthDailyCheckInIsAllowed()
    {
        _repository.Setup(r => r.CountSinceAsync(_userId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(5);

        Assert.True((await Create(Request())).IsSuccess);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(7)]
    public async Task DailyLimitStillAppliesAcrossShops(int todayCount)
    {
        _repository.Setup(r => r.CountSinceAsync(_userId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(todayCount);

        var error = await Assert.ThrowsAsync<ValidationException>(() => Create(Request()));

        Assert.Contains("6 per day", error.Message);
        _repository.Verify(r => r.Add(It.IsAny<DomainCheckIn>()), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _bus.Verify(b => b.PublishAsync(It.IsAny<CheckinCreatedEvent>()), Times.Never);
        _bus.Verify(b => b.PublishAsync(It.IsAny<CheckInCacheInvalidationRequested>()), Times.Never);
    }

    [Fact]
    public async Task MissingShopOrInactiveDrinkFailsBeforeSave()
    {
        _shops.Setup(s => s.Exists(_shopId, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        await Assert.ThrowsAsync<NotFoundException>(() => Create(Request()));
        _shops.Setup(s => s.Exists(_shopId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        await Assert.ThrowsAsync<ValidationException>(() => Create(Request() with { DrinkSlug = "inactive" }));
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _bus.Verify(b => b.PublishAsync(It.IsAny<CheckinCreatedEvent>()), Times.Never);
    }

    [Fact]
    public async Task AlreadyAttachedPhotoFailsBeforeMediaMove()
    {
        _repository.Setup(r => r.ArePhotoKeysUsedAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        await Assert.ThrowsAsync<ValidationException>(() => Create(Request() with
            { Photos = [new UploadedPhotoDto("ignored", "ignored", "key", 1)] }));
        _photos.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ForeignOwnerCannotEditOrDelete()
    {
        var checkIn = DomainCheckIn.CreateUnified(Guid.NewGuid(), _shopId, "other", "x", Rating.Create(1, 1, 1), DateTime.UtcNow);
        _repository.Setup(r => r.GetTrackedByIdAsync(checkIn.Id, It.IsAny<CancellationToken>())).ReturnsAsync(checkIn);
        await Assert.ThrowsAsync<NotFoundException>(() => UpdateUnifiedCheckInHandler.Handle(
            new DeleteUnifiedCheckInCommand(checkIn.Id, _userId), _repository.Object, _unitOfWork.Object, _bus.Object, CancellationToken.None));
        await Assert.ThrowsAsync<NotFoundException>(() => UpdateUnifiedCheckInHandler.Handle(
            new UpdateUnifiedCheckInCommand(checkIn.Id, _userId, new("updated", Request().Rating)), _repository.Object,
            _drinks.Object, _photos.Object, _unitOfWork.Object, _bus.Object, CancellationToken.None));
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _bus.Verify(b => b.PublishAsync(It.IsAny<CheckinCreatedEvent>()), Times.Never);
    }

    [Fact]
    public async Task LostClaimReplyRetainsRetryIdentityAndNeverReleasesAnotherAttempt()
    {
        var request=Request() with {Photos=[new UploadedPhotoDto("a.jpg","image/jpeg","upload-key",1)]};
        Guid reserved=Guid.Empty;
        var attempts=0;
        _photos.Setup(p=>p.ClaimForCheckInAsync(_userId,It.IsAny<Guid>(),It.IsAny<IReadOnlyCollection<string>>(),default))
            .Returns((Guid _,Guid id,IReadOnlyCollection<string> keys,CancellationToken _) =>
            {
                if (reserved==Guid.Empty) reserved=id;
                Assert.Equal(reserved,id);
                return Task.FromResult(++attempts==1 ? CheckInPhotoClaimResult.Failed : new CheckInPhotoClaimResult(true,
                    [new ClaimedCheckInPhotoDto(Guid.NewGuid(),"upload-key","check-ins/immutable.jpg","a.jpg","image/jpeg",1)]));
            });
        await Assert.ThrowsAsync<ValidationException>(()=>Create(request));
        var saved=(await Create(request)).Data;
        Assert.Equal(reserved,saved.Id);
        _photos.Verify(p=>p.ReleaseCheckInClaimAsync(It.IsAny<Guid>(),It.IsAny<Guid>(),It.IsAny<CancellationToken>()),Times.Never());
        _repository.Setup(r=>r.GetTrackedByIdAsync(reserved,default)).ReturnsAsync(DomainCheckIn.CreateUnified(_userId,_shopId,"author","x",Rating.Create(3,4,5),DateTime.UtcNow,id:reserved));
        await Assert.ThrowsAsync<ConflictException>(()=>Create(request));
        _photos.Verify(p=>p.ClaimForCheckInAsync(_userId,reserved,It.IsAny<IReadOnlyCollection<string>>(),default),Times.Exactly(2));
        _bus.Verify(b=>b.PublishAsync(It.IsAny<CheckinCreatedEvent>()),Times.Once);
    }

    [Fact]
    public async Task AcceptedCatalogDrinkAppearsInCreateAndEditResponses()
    {
        var drink=CoffeeDrinkDefinition.CreateWithId(Guid.NewGuid(),"latte","Латте","Latte",CoffeeDrinkCategory.Espresso,"",0);
        _drinks.Setup(d=>d.FindAsync("latte",default)).ReturnsAsync(new DrinkCatalogEntry(drink.NameRu,drink.NameEn,true));
        var result=await Create(Request() with {DrinkSlug=drink.Slug});
        Assert.Equal(new CheckInDrinkDto("latte",null,"Латте","Latte"),result.Data.Drink);
        var row=DomainCheckIn.CreateUnified(_userId,_shopId,"author","x",Rating.Create(3,4,5),DateTime.UtcNow);
        _repository.Setup(r=>r.GetTrackedByIdAsync(row.Id,default)).ReturnsAsync(row);
        var edited=await UpdateUnifiedCheckInHandler.Handle(new UpdateUnifiedCheckInCommand(row.Id,_userId,new("edited",Request().Rating,"latte")),_repository.Object,_drinks.Object,_photos.Object,_unitOfWork.Object,_bus.Object,default);
        Assert.Equal(result.Data.Drink,edited.Data.Drink);
        _bus.Verify(b => b.PublishAsync(It.Is<CheckInCacheInvalidationRequested>(message =>
            message.CheckInId == row.Id && message.ShopId == _shopId && message.UserId == _userId)), Times.Once);
    }

    [Fact]
    public async Task DeletingOwnCheckInPublishesPostCommitInvalidation()
    {
        var row = DomainCheckIn.CreateUnified(_userId, _shopId, "author", "visit",
            Rating.Create(4, 4, 4), DateTime.UtcNow);
        _repository.Setup(r => r.GetTrackedByIdAsync(row.Id, default)).ReturnsAsync(row);

        var result = await UpdateUnifiedCheckInHandler.Handle(
            new DeleteUnifiedCheckInCommand(row.Id, _userId), _repository.Object,
            _unitOfWork.Object, _bus.Object, default);

        Assert.True(result.IsSuccess);
        Assert.NotNull(row.DeletedAtUtc);
        _unitOfWork.Verify(u => u.SaveChangesAsync(default), Times.Once);
        _bus.Verify(b => b.PublishAsync(It.Is<CheckInCacheInvalidationRequested>(message =>
            message.CheckInId == row.Id && message.ShopId == _shopId && message.UserId == _userId)), Times.Once);
    }

    [Fact]
    public async Task HistoricalUnboundPhotoCannotBePromotedToPublic()
    {
        var row=DomainCheckIn.CreateUnified(_userId,_shopId,"author","visit",Rating.Create(4,4,4),DateTime.UtcNow.AddYears(-1));
        row.AddPhotos([new ShopPhoto("old.jpg","image/jpeg","old-public-key",10,_userId)]);
        _repository.Setup(r=>r.GetTrackedByIdAsync(row.Id,default)).ReturnsAsync(row);
        _photos.Setup(p=>p.ClaimForCheckInAsync(_userId,row.Id,It.IsAny<IReadOnlyCollection<string>>(),default)).ReturnsAsync(CheckInPhotoClaimResult.Failed);
        await Assert.ThrowsAsync<ConflictException>(()=>UpdateUnifiedCheckInHandler.Handle(new ChangeUnifiedCheckInVisibilityCommand(row.Id,_userId,Visibility.Public),
            _repository.Object,_photos.Object,_unitOfWork.Object,_bus.Object,default));
        Assert.False(row.IsPubliclyVisible);Assert.Equal(Domain.Aggregates.CheckInAggregate.CheckInVisibility.Private,row.Visibility);
        _bus.Verify(b=>b.PublishAsync(It.IsAny<object>()),Times.Never());
    }
}
