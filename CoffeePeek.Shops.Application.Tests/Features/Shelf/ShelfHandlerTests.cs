using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Dtos.Shelf;
using CoffeePeek.Contract.Events;
using CoffeePeek.Contract.Events.Account;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Application.Features.Shelf;
using CoffeePeek.Shops.Application.Features.Shelf.CreateShelfBag;
using CoffeePeek.Shops.Application.Features.Shelf.DeleteAccountShelf;
using CoffeePeek.Shops.Application.Features.Shelf.DeleteShelfBag;
using CoffeePeek.Shops.Application.Features.Shelf.FinishShelfBag;
using CoffeePeek.Shops.Application.Features.Shelf.ReorderShelf;
using CoffeePeek.Shops.Application.Features.Shelf.RestoreShelfBag;
using CoffeePeek.Shops.Application.Features.Shelf.UpdateShelfBag;
using CoffeePeek.Shops.Domain.Aggregates.ShelfAggregate;
using Moq;
using Wolverine;
using ContractGrind = CoffeePeek.Contract.Enums.ShelfBagGrind;
using ContractRoastPurpose = CoffeePeek.Contract.Enums.ShelfBagRoastPurpose;
using ContractStatus = CoffeePeek.Contract.Enums.ShelfBagStatus;

namespace CoffeePeek.Shops.Application.Tests.Features.Shelf;

public class ShelfHandlerTests
{
    private const string UploadKey = "shelf-bags/7b2d0b8e-1c55-4b0e-9d7e-5f0b7c9a01e1.jpg";
    private static readonly Guid ProductId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private readonly Guid _owner = Guid.NewGuid();
    private readonly List<ShelfBag> _rows = [];
    private readonly Mock<IShelfBagRepository> _bags = new();
    private readonly Mock<IShelfCatalogLookup> _catalog = new();
    private readonly Mock<IShelfBagPhotoStore> _photos = new();
    private readonly Mock<IMessageBus> _bus = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private CancellationToken Ct => TestContext.Current.CancellationToken;

    public ShelfHandlerTests()
    {
        _bags.Setup(b => b.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => _rows.SingleOrDefault(r => r.Id == id));
        _bags.Setup(b => b.GetOnShelfAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid user, CancellationToken _) =>
                _rows.Where(r => r.UserId == user && r.Status == ShelfBagStatus.OnShelf).OrderBy(r => r.Position).ToList());
        _bags.Setup(b => b.GetAllAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid user, CancellationToken _) => _rows.Where(r => r.UserId == user).ToList());
        _bags.Setup(b => b.Add(It.IsAny<ShelfBag>())).Callback((ShelfBag bag) => _rows.Add(bag));
        _bags.Setup(b => b.Remove(It.IsAny<ShelfBag>())).Callback((ShelfBag bag) => _rows.Remove(bag));
        _catalog.Setup(c => c.FindPublishedBySlugAsync("nut-n-berry", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ShelfCatalogProduct(ProductId, "Nut'n'Berry", "Sibaristica"));
        _catalog.Setup(c => c.GetPublishedSlugsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string> { [ProductId] = "nut-n-berry" });
    }

    private Task<CoffeePeek.Shared.Kernel.Response.Response<ShelfBagDto>> Create(CreateShelfBagRequest request, Guid? user = null) =>
        CreateShelfBagHandler.Handle(new CreateShelfBagCommand(user ?? _owner, request), _bags.Object, _catalog.Object,
            _photos.Object, _unitOfWork.Object, Ct);

    private Task<CoffeePeek.Shared.Kernel.Response.Response<ShelfBagDto>> Update(Guid id, UpdateShelfBagRequest request, Guid? user = null) =>
        UpdateShelfBagHandler.Handle(new UpdateShelfBagCommand(user ?? _owner, id, request), _bags.Object, _catalog.Object,
            _photos.Object, _bus.Object, _unitOfWork.Object, Ct);

    private async Task<ShelfBagDto> Custom(string name) => (await Create(new CreateShelfBagRequest { CustomName = name })).Data;

    private void ClaimReturns(ShelfBagPhotoClaimStatus status, Guid? photoId = null) =>
        _photos.Setup(p => p.ClaimAsync(_owner, It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, Guid bag, string _, CancellationToken _) => new ShelfBagPhotoClaimResult(status,
                status == ShelfBagPhotoClaimStatus.Claimed
                    ? new ClaimedShelfBagPhotoDto(photoId ?? Guid.NewGuid(), $"shelf-bags/{bag:N}.jpg", "image/jpeg", 98304) : null));

    private static UploadedPhotoDto Upload(string key = UploadKey) => new("bag.jpg", "image/jpeg", key, 98304);

    [Fact]
    public async Task CatalogSelectionStoresIdentityNameSnapshotAndDefaultRoasterAtTheFront()
    {
        var older = await Custom("Older bag");
        var result = await Create(new CreateShelfBagRequest
        {
            CoffeeProductSlug = " Nut-N-Berry ", WeightGrams = 250, Grind = ContractGrind.Beans,
            RoastPurpose = ContractRoastPurpose.Filter, RoastedOn = new DateOnly(2026, 10, 12)
        });

        Assert.True(result.IsSuccess);
        var dto = result.Data;
        Assert.Equal(ContractStatus.OnShelf, dto.Status);
        Assert.Equal(0, dto.Position);
        Assert.Equal(ProductId, dto.CoffeeProductId);
        Assert.Equal("nut-n-berry", dto.CoffeeProductSlug);
        Assert.Equal("Nut'n'Berry", dto.Name);
        Assert.Equal("Sibaristica", dto.RoasterName);
        Assert.Equal(ContractGrind.Beans, dto.Grind);
        Assert.Equal(ContractRoastPurpose.Filter, dto.RoastPurpose);
        Assert.Null(dto.Photo);
        Assert.Equal(1, _rows.Single(r => r.Id == older.Id).Position);
        _bags.Verify(b => b.LockOwnerAsync(_owner, It.IsAny<CancellationToken>()), Times.Exactly(2));
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task SuppliedRoasterOverridesTheCatalogRoaster()
    {
        var dto = (await Create(new CreateShelfBagRequest { CoffeeProductSlug = "nut-n-berry", RoasterName = "Gift from a friend" })).Data;
        Assert.Equal("Gift from a friend", dto.RoasterName);
    }

    [Fact]
    public async Task InvalidBeanSelectionStopsBeforeLockingOrClaiming()
    {
        await Assert.ThrowsAsync<ValidationException>(() => Create(new CreateShelfBagRequest
            { CoffeeProductSlug = "nut-n-berry", CustomName = "Also custom", Photo = Upload() }));
        await Assert.ThrowsAsync<ValidationException>(() => Create(new CreateShelfBagRequest { WeightGrams = 250 }));
        await Assert.ThrowsAsync<ValidationException>(() => Create(new CreateShelfBagRequest { CustomName = new string('x', 251) }));
        await Assert.ThrowsAsync<NotFoundException>(() => Create(new CreateShelfBagRequest { CoffeeProductSlug = "archived-coffee" }));
        await Assert.ThrowsAsync<ValidationException>(() => Create(new CreateShelfBagRequest { Photo = Upload("check-ins/x.jpg") }));
        await Assert.ThrowsAsync<DomainException>(() => Create(new CreateShelfBagRequest { Photo = Upload(), WeightGrams = 0 }));
        await Assert.ThrowsAsync<DomainException>(() => Create(new CreateShelfBagRequest
            { Photo = Upload(), RoastedOn = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(2) }));
        _bags.Verify(b => b.LockOwnerAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _photos.VerifyNoOtherCalls();
        Assert.Empty(_rows);
    }

    [Fact]
    public async Task CardOnlyBagClaimsTheUploadForAStableBagId()
    {
        var photoId = Guid.NewGuid();
        ClaimReturns(ShelfBagPhotoClaimStatus.Claimed, photoId);

        var dto = (await Create(new CreateShelfBagRequest { Photo = Upload() })).Data;

        Assert.Equal(ShelfBagInput.CreateId(_owner, UploadKey), dto.Id);
        Assert.Equal(photoId, dto.Photo!.Id);
        Assert.Equal($"/api/v1/shelf/bags/{dto.Id}/photos/{photoId}", dto.Photo.Url);
        Assert.Null(dto.Name);
        _photos.Verify(p => p.ClaimAsync(_owner, dto.Id, UploadKey, It.IsAny<CancellationToken>()), Times.Once);

        // A retry of a committed create cannot attach the same upload to a second bag.
        await Assert.ThrowsAsync<ConflictException>(() => Create(new CreateShelfBagRequest { Photo = Upload() }));
        Assert.Single(_rows);
    }

    [Theory]
    [InlineData(ShelfBagPhotoClaimStatus.AttachedElsewhere, typeof(ConflictException))]
    [InlineData(ShelfBagPhotoClaimStatus.Rejected, typeof(ValidationException))]
    [InlineData(ShelfBagPhotoClaimStatus.Unavailable, typeof(ServiceUnavailableException))]
    public async Task ClaimFailuresMapToClientErrors(ShelfBagPhotoClaimStatus status, Type expected)
    {
        ClaimReturns(status);
        await Assert.ThrowsAsync(expected, () => Create(new CreateShelfBagRequest { Photo = Upload(), CustomName = "x" }));
        Assert.Empty(_rows);
    }

    [Fact]
    public async Task FullShelfIsAConflictBeforeTheUploadIsClaimed()
    {
        for (var i = 0; i < ShelfLineup.Capacity; i++) await Custom($"bag {i}");
        await Assert.ThrowsAsync<ConflictException>(() => Create(new CreateShelfBagRequest { Photo = Upload(), CustomName = "x" }));
        _photos.VerifyNoOtherCalls();
        Assert.Equal(ShelfLineup.Capacity, _rows.Count);
    }

    [Fact]
    public async Task DuplicatesAreSeparateBags()
    {
        await Create(new CreateShelfBagRequest { CoffeeProductSlug = "nut-n-berry" });
        await Create(new CreateShelfBagRequest { CoffeeProductSlug = "nut-n-berry" });
        Assert.Equal(2, _rows.Count(r => r.CoffeeProductId == ProductId));
    }

    [Fact]
    public async Task UpdateKeepsTheProductSnapshotAndClearsOmittedDetails()
    {
        var created = (await Create(new CreateShelfBagRequest
        {
            CoffeeProductSlug = "nut-n-berry", WeightGrams = 250, RoastPurpose = ContractRoastPurpose.Filter
        })).Data;
        _catalog.Setup(c => c.GetPublishedSlugsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string>());

        var updated = (await Update(created.Id, new UpdateShelfBagRequest { CoffeeProductId = ProductId, Grind = ContractGrind.Ground })).Data;

        Assert.Equal(ProductId, updated.CoffeeProductId);
        Assert.Null(updated.CoffeeProductSlug);
        Assert.Equal("Nut'n'Berry", updated.Name);
        Assert.Null(updated.RoasterName);
        Assert.Null(updated.WeightGrams);
        Assert.Equal(ContractGrind.Ground, updated.Grind);
        Assert.Null(updated.RoastPurpose);
        Assert.Equal(0, updated.Position);
        await Assert.ThrowsAsync<ValidationException>(() => Update(created.Id, new UpdateShelfBagRequest { CoffeeProductId = Guid.NewGuid() }));
        await Assert.ThrowsAsync<ValidationException>(() => Update(created.Id,
            new UpdateShelfBagRequest { CoffeeProductId = ProductId, CustomName = "x" }));
        await Assert.ThrowsAsync<ValidationException>(() => Update(created.Id, new UpdateShelfBagRequest()));
    }

    [Fact]
    public async Task AnotherOwnersBagIsNotFound()
    {
        var created = await Custom("Mine");
        var stranger = Guid.NewGuid();
        await Assert.ThrowsAsync<NotFoundException>(() => Update(created.Id, new UpdateShelfBagRequest { CustomName = "x" }, stranger));
        await Assert.ThrowsAsync<NotFoundException>(() => DeleteShelfBagHandler.Handle(new DeleteShelfBagCommand(stranger, created.Id),
            _bags.Object, _bus.Object, _unitOfWork.Object, Ct));
        await Assert.ThrowsAsync<NotFoundException>(() => FinishShelfBagHandler.Handle(new FinishShelfBagCommand(stranger, created.Id, null, null),
            _bags.Object, _catalog.Object, _unitOfWork.Object, Ct));
        Assert.Equal("Mine", _rows.Single().Name);
    }

    [Fact]
    public async Task ReplacingTheCardReleasesThePreviousOneAndOmittingItKeepsIt()
    {
        var first = Guid.NewGuid();
        ClaimReturns(ShelfBagPhotoClaimStatus.Claimed, first);
        var created = (await Create(new CreateShelfBagRequest { Photo = Upload() })).Data;

        var kept = (await Update(created.Id, new UpdateShelfBagRequest { CustomName = "Named later" })).Data;
        Assert.Equal(first, kept.Photo!.Id);
        _bus.Verify(b => b.PublishAsync(It.IsAny<ShelfBagPhotoReleasedEvent>(), It.IsAny<DeliveryOptions>()), Times.Never);

        var second = Guid.NewGuid();
        ClaimReturns(ShelfBagPhotoClaimStatus.Claimed, second);
        var replaced = (await Update(created.Id, new UpdateShelfBagRequest
            { CustomName = "Named later", Photo = Upload("shelf-bags/retake.jpg") })).Data;

        Assert.Equal(second, replaced.Photo!.Id);
        _photos.Verify(p => p.ClaimAsync(_owner, created.Id, "shelf-bags/retake.jpg", It.IsAny<CancellationToken>()), Times.Once);
        _bus.Verify(b => b.PublishAsync(It.Is<ShelfBagPhotoReleasedEvent>(e =>
            e.PhotoId == first && e.BagId == created.Id && e.UserId == _owner), It.IsAny<DeliveryOptions>()), Times.Once);
    }

    [Fact]
    public async Task ReorderFinishRestoreAndDeleteKeepPositionsContiguous()
    {
        var c = await Custom("c");
        var b = await Custom("b");
        var a = await Custom("a");

        var reordered = (await ReorderShelfHandler.Handle(new ReorderShelfCommand(_owner, [c.Id, a.Id, b.Id]),
            _bags.Object, _catalog.Object, _unitOfWork.Object, Ct)).Data;
        Assert.Equal([c.Id, a.Id, b.Id], reordered.Select(x => x.Id));
        Assert.Equal([0, 1, 2], reordered.Select(x => x.Position!.Value));
        await Assert.ThrowsAsync<ConflictException>(() => ReorderShelfHandler.Handle(new ReorderShelfCommand(_owner, [c.Id, a.Id]),
            _bags.Object, _catalog.Object, _unitOfWork.Object, Ct));

        var finished = (await FinishShelfBagHandler.Handle(new FinishShelfBagCommand(_owner, a.Id, 4, true),
            _bags.Object, _catalog.Object, _unitOfWork.Object, Ct)).Data;
        Assert.Equal(ContractStatus.Finished, finished.Status);
        Assert.Null(finished.Position);
        Assert.NotNull(finished.FinishedAtUtc);
        Assert.Equal(4, finished.Rating);
        Assert.True(finished.BuyAgain);
        Assert.Equal(1, _rows.Single(r => r.Id == b.Id).Position);
        await Assert.ThrowsAsync<ConflictException>(() => FinishShelfBagHandler.Handle(new FinishShelfBagCommand(_owner, a.Id, null, null),
            _bags.Object, _catalog.Object, _unitOfWork.Object, Ct));

        var restored = (await RestoreShelfBagHandler.Handle(new RestoreShelfBagCommand(_owner, a.Id, 1),
            _bags.Object, _catalog.Object, _unitOfWork.Object, Ct)).Data;
        Assert.Equal(1, restored.Position);
        Assert.Null(restored.Rating);
        await Assert.ThrowsAsync<ConflictException>(() => RestoreShelfBagHandler.Handle(new RestoreShelfBagCommand(_owner, a.Id, 0),
            _bags.Object, _catalog.Object, _unitOfWork.Object, Ct));

        await DeleteShelfBagHandler.Handle(new DeleteShelfBagCommand(_owner, c.Id), _bags.Object, _bus.Object, _unitOfWork.Object, Ct);
        Assert.Equal([a.Id, b.Id], _rows.OrderBy(r => r.Position).Select(r => r.Id));
        Assert.Equal([0, 1], _rows.OrderBy(r => r.Position).Select(r => r.Position!.Value));
    }

    [Fact]
    public async Task DeletingABagWithACardReleasesIt()
    {
        var photoId = Guid.NewGuid();
        ClaimReturns(ShelfBagPhotoClaimStatus.Claimed, photoId);
        var created = (await Create(new CreateShelfBagRequest { Photo = Upload() })).Data;

        var response = await DeleteShelfBagHandler.Handle(new DeleteShelfBagCommand(_owner, created.Id), _bags.Object, _bus.Object,
            _unitOfWork.Object, Ct);

        Assert.True(response.IsSuccess);
        Assert.Empty(_rows);
        _bus.Verify(b => b.PublishAsync(It.Is<ShelfBagPhotoReleasedEvent>(e => e.PhotoId == photoId && e.BagId == created.Id),
            It.IsAny<DeliveryOptions>()), Times.Once);
    }

    [Fact]
    public async Task AccountDeletionRemovesTheWholeShelfOnlyForThatUser()
    {
        var photoId = Guid.NewGuid();
        ClaimReturns(ShelfBagPhotoClaimStatus.Claimed, photoId);
        var carded = (await Create(new CreateShelfBagRequest { Photo = Upload() })).Data;
        var archived = await Custom("archived");
        await FinishShelfBagHandler.Handle(new FinishShelfBagCommand(_owner, archived.Id, null, null), _bags.Object, _catalog.Object,
            _unitOfWork.Object, Ct);
        var someoneElse = Guid.NewGuid();
        await Create(new CreateShelfBagRequest { CustomName = "other" }, someoneElse);

        var message = new AccountDeletionRequestedV1(Guid.NewGuid(), _owner, DateTime.UtcNow);
        await DeleteAccountShelfHandler.Handle(message, _bags.Object, _bus.Object, _unitOfWork.Object, Ct);
        await DeleteAccountShelfHandler.Handle(message, _bags.Object, _bus.Object, _unitOfWork.Object, Ct);

        Assert.Equal(someoneElse, Assert.Single(_rows).UserId);
        _bus.Verify(b => b.PublishAsync(It.Is<ShelfBagPhotoReleasedEvent>(e => e.PhotoId == photoId && e.BagId == carded.Id),
            It.IsAny<DeliveryOptions>()), Times.Once);
    }
}
