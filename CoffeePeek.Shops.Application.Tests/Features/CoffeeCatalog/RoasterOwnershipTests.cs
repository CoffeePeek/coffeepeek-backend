using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Dtos.Shop;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Options;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shops.Application.Features.Admin.Catalogs.Roasters;
using CoffeePeek.Shops.Application.Features.Catalogs.GetRoasterById;
using CoffeePeek.Shops.Application.Features.CoffeeCatalog;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using CoffeePeek.Shops.Domain.Entities;
using MapsterMapper;
using Microsoft.Extensions.Options;
using Moq;

namespace CoffeePeek.Shops.Application.Tests.Features.CoffeeCatalog;

public class RoasterOwnershipTests
{
    [Fact]
    public async Task AssignmentAndRevocationAdvanceRevisionOnlyOnChange()
    {
        var roaster = new Roaster("Roaster");
        var repository = new Mock<IRoasterRepository>();
        repository.Setup(r => r.GetByIdAsync(roaster.Id, It.IsAny<CancellationToken>())).ReturnsAsync(roaster);
        var writer = new RoasterOwnershipWriter(repository.Object);
        var owner = Guid.NewGuid();

        Assert.Null(await writer.AssignAsync(roaster.Id, owner, true, CancellationToken.None));
        Assert.Equal(owner, roaster.OwnerUserId);
        Assert.Equal(1, roaster.ProfileRevision);
        Assert.Equal(owner, await writer.AssignAsync(roaster.Id, owner, false, CancellationToken.None));
        Assert.Equal(1, roaster.ProfileRevision);
        Assert.Equal(owner, await writer.AssignAsync(roaster.Id, null, false, CancellationToken.None));
        Assert.Null(roaster.OwnerUserId);
        Assert.Equal(2, roaster.ProfileRevision);
    }

    [Fact]
    public async Task ClaimCannotReplaceAnExistingOwner()
    {
        var roaster = new Roaster("Roaster");
        var owner = Guid.NewGuid();
        roaster.AssignOwner(owner);
        var repository = new Mock<IRoasterRepository>();
        repository.Setup(r => r.GetByIdAsync(roaster.Id, It.IsAny<CancellationToken>())).ReturnsAsync(roaster);

        await Assert.ThrowsAsync<ConflictException>(() => new RoasterOwnershipWriter(repository.Object)
            .AssignAsync(roaster.Id, Guid.NewGuid(), true, CancellationToken.None));

        Assert.Equal(owner, roaster.OwnerUserId);
        Assert.Equal(1, roaster.ProfileRevision);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForeignOrRevokedOwnerCannotChangeProfileOrClaimPhotos(bool revoke)
    {
        var owner = Guid.NewGuid();
        var roaster = new Roaster("Original");
        roaster.AssignOwner(owner);
        if (revoke) roaster.AssignOwner(null);
        var actor = revoke ? owner : Guid.NewGuid();
        var repository = new Mock<IRoasterRepository>();
        repository.Setup(r => r.GetByIdAsync(roaster.Id, It.IsAny<CancellationToken>())).ReturnsAsync(roaster);
        var photos = new Mock<IUploadedPhotoLookup>(MockBehavior.Strict);
        var bus = new Mock<Wolverine.IMessageBus>(MockBehavior.Strict);
        var revision = roaster.ProfileRevision;

        var response = await UpdateRoasterHandler.Handle(new UpdateRoasterCommand(roaster.Id, "Changed",
                Photos: [new UploadedPhotoDto("file", "image/jpeg", "new-key", 1)], ActorUserId: actor)
            { OwnerUserId = actor, ExpectedRevision = revision }, repository.Object,
            Mock.Of<IMapper>(), photos.Object, bus.Object, CancellationToken.None);

        Assert.Equal((int)HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Original", roaster.Name);
        Assert.Equal(revision, roaster.ProfileRevision);
        photos.VerifyNoOtherCalls();
        bus.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task StaleOwnerUpdateDoesNotMutateOrClaimPhotos()
    {
        var owner = Guid.NewGuid();
        var roaster = new Roaster("Original");
        roaster.AssignOwner(owner);
        var repository = new Mock<IRoasterRepository>();
        repository.Setup(r => r.GetByIdAsync(roaster.Id, It.IsAny<CancellationToken>())).ReturnsAsync(roaster);
        var photos = new Mock<IUploadedPhotoLookup>(MockBehavior.Strict);

        var response = await UpdateRoasterHandler.Handle(new UpdateRoasterCommand(roaster.Id, "Changed", ActorUserId: owner)
            { OwnerUserId = owner, ExpectedRevision = 0 }, repository.Object, Mock.Of<IMapper>(),
            photos.Object, Mock.Of<Wolverine.IMessageBus>(), CancellationToken.None);

        Assert.Equal((int)HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Original", roaster.Name);
        Assert.Equal(1, roaster.ProfileRevision);
        photos.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CurrentOwnerCanSaveAndRetainOrClearPreviousOwnersPhotos(bool clear)
    {
        var owner = Guid.NewGuid();
        var roaster = new Roaster("Original");
        roaster.AssignOwner(owner);
        var photo = new RoasterPhoto("old.jpg", "image/jpeg", "old-key", 10, Guid.NewGuid());
        roaster.ReplacePhotos([photo]);
        var repository = new Mock<IRoasterRepository>();
        repository.Setup(r => r.GetByIdAsync(roaster.Id, It.IsAny<CancellationToken>())).ReturnsAsync(roaster);
        var mapper = new Mock<IMapper>();
        mapper.Setup(m => m.Map<RoasterDto>(roaster)).Returns(new RoasterDto { Id = roaster.Id });
        var photos = new Mock<IUploadedPhotoLookup>(MockBehavior.Strict);

        var response = await UpdateRoasterHandler.Handle(new UpdateRoasterCommand(roaster.Id, "Changed",
                Photos: clear ? [] : null, ActorUserId: owner)
            { OwnerUserId = owner, ExpectedRevision = 1 }, repository.Object, mapper.Object, photos.Object,
            Mock.Of<Wolverine.IMessageBus>(), CancellationToken.None);

        Assert.True(response.IsSuccess);
        Assert.Equal("Changed", roaster.Name);
        Assert.Equal(2, roaster.ProfileRevision);
        Assert.Equal(2, response.Data.ProfileRevision);
        if (clear) Assert.Empty(roaster.Photos);
        else Assert.Same(photo, Assert.Single(roaster.Photos));
        photos.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task OwnerReadRejectsForeignProfileBeforeReadingCatalogDetails()
    {
        var roaster = new Roaster("Private owner profile");
        roaster.AssignOwner(Guid.NewGuid());
        var repository = new Mock<IQueryRoasterRepository>();
        repository.Setup(r => r.GetByIdWithShopsAndPhotosAsync(roaster.Id, It.IsAny<CancellationToken>())).ReturnsAsync(roaster);
        var catalog = new Mock<IPublicCoffeeCatalogReader>(MockBehavior.Strict);

        var response = await GetRoasterByIdHandler.Handle(new GetRoasterByIdQuery(roaster.Id, Guid.NewGuid()),
            repository.Object, Options.Create(new MediaPublicUrlOptions()), catalog.Object, CancellationToken.None);

        Assert.Equal((int)HttpStatusCode.NotFound, response.StatusCode);
        catalog.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 101)]
    [InlineData(int.MaxValue, 100)]
    public async Task OwnerListRejectsInvalidPaging(int page, int pageSize)
    {
        await Assert.ThrowsAsync<ValidationException>(() => GetOwnerRoastersHandler.Handle(
            new GetOwnerRoastersQuery(Guid.NewGuid(), page, pageSize), Mock.Of<IQueryRoasterRepository>(),
            Mock.Of<IMapper>(), CancellationToken.None));
    }
}
