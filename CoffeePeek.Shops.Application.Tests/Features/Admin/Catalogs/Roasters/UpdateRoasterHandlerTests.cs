using System;
using System.Net;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Dtos.Shop;
using CoffeePeek.Shops.Application.Features.Admin.Catalogs.Roasters;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using FluentAssertions;
using MapsterMapper;
using Moq;
using Wolverine;
using CoffeePeek.Contract.Dtos.ShopPhotos;
using CoffeePeek.Contract.Events;
using CoffeePeek.Shops.Domain.Entities;

namespace CoffeePeek.Shops.Application.Tests.Features.Admin.Catalogs.Roasters;

public class UpdateRoasterHandlerTests
{
    private readonly Mock<IRoasterRepository> _repo = new();
    private readonly Mock<IMapper> _mapper = new();
    private readonly Mock<IUploadedPhotoLookup> _photos = new();
    private readonly CancellationToken _ct = CancellationToken.None;

    [Fact]
    public async Task Handle_ExistingRoaster_UpdatesNameAndReturnsSuccess()
    {
        var roaster = new Roaster("Coffee Circus");
        _repo.Setup(r => r.GetByIdAsync(roaster.Id, _ct)).ReturnsAsync(roaster);
        _mapper.Setup(m => m.Map<RoasterDto>(It.IsAny<Roaster>()))
            .Returns((Roaster r) => new RoasterDto { Id = r.Id, Name = r.Name });

        var result = await UpdateRoasterHandler.Handle(
            new UpdateRoasterCommand(roaster.Id, "Grunwald Coffee Roasters"),
            _repo.Object, _mapper.Object, _photos.Object, Mock.Of<IMessageBus>(), _ct);

        result.IsSuccess.Should().BeTrue();
        result.Data.Name.Should().Be("Grunwald Coffee Roasters");
        roaster.Name.Should().Be("Grunwald Coffee Roasters");
    }

    [Fact]
    public async Task Handle_MissingRoaster_ReturnsNotFound()
    {
        _repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), _ct)).ReturnsAsync((Roaster)null);

        var result = await UpdateRoasterHandler.Handle(
            new UpdateRoasterCommand(Guid.NewGuid(), "Grunwald Coffee Roasters"),
            _repo.Object, _mapper.Object, _photos.Object, Mock.Of<IMessageBus>(), _ct);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be((int)HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Replacement_RetainsExistingIdentityAndClaimsOnlyAdditions()
    {
        var actor = Guid.NewGuid();
        var roaster = new Roaster("Original");
        var old = new RoasterPhoto("old.jpg", "image/jpeg", "old-key", 10, Guid.NewGuid(), 3);
        roaster.ReplacePhotos([old]);
        _repo.Setup(r => r.GetByIdAsync(roaster.Id, _ct)).ReturnsAsync(roaster);
        _mapper.Setup(m => m.Map<RoasterDto>(It.IsAny<Roaster>())).Returns(new RoasterDto());
        var addedId = Guid.NewGuid();
        _photos.Setup(p => p.ClaimForShopAsync(actor, roaster.Id, ShopPhotoAttachmentKind.Roaster,
                It.Is<IReadOnlyCollection<string>>(keys => keys.SequenceEqual(new[] { "new-key" })), _ct))
            .ReturnsAsync(new ShopPhotoClaimResult(true, [new(addedId, "new-key", "trusted.jpg", "image/jpeg", 20)]));
        var bus = new Mock<IMessageBus>();
        var command = new UpdateRoasterCommand(roaster.Id, "Changed", ActorUserId: actor,
            Photos: [new("untrusted", "untrusted", "new-key", -1), new("untrusted", "untrusted", "old-key", -1)]);

        await UpdateRoasterHandler.Handle(command, _repo.Object, _mapper.Object, _photos.Object, bus.Object, _ct);
        roaster.Photos.Should().ContainSingle(p => p.Id == addedId && p.SortIndex == 0 && p.SizeBytes == 20);
        roaster.Photos.Should().ContainSingle(p => ReferenceEquals(p, old) && p.SortIndex == 1 && p.FileName == "old.jpg");
        bus.Verify(b => b.PublishAsync(It.Is<ShopPhotosAttachedEvent>(e => e.ShopId == roaster.Id
            && e.Kind == ShopPhotoAttachmentKind.Roaster && e.StorageKeys.SequenceEqual(new[] { "new-key" }))), Times.Once);

        await UpdateRoasterHandler.Handle(command, _repo.Object, _mapper.Object, _photos.Object, bus.Object, _ct);
        _photos.Verify(p => p.ClaimForShopAsync(actor, roaster.Id, ShopPhotoAttachmentKind.Roaster,
            It.IsAny<IReadOnlyCollection<string>>(), _ct), Times.Once);
        bus.Verify(b => b.PublishAsync(It.IsAny<ShopPhotosAttachedEvent>()), Times.Once);
    }

    [Fact]
    public async Task FailedClaim_DoesNotMutateProfileOrExistingPhotos()
    {
        var roaster = new Roaster("Original");
        var old = new RoasterPhoto("old.jpg", "image/jpeg", "old-key", 10, Guid.NewGuid());
        roaster.ReplacePhotos([old]);
        _repo.Setup(r => r.GetByIdAsync(roaster.Id, _ct)).ReturnsAsync(roaster);
        _photos.Setup(p => p.ClaimForShopAsync(It.IsAny<Guid>(), roaster.Id, ShopPhotoAttachmentKind.Roaster,
                It.IsAny<IReadOnlyCollection<string>>(), _ct)).ReturnsAsync(ShopPhotoClaimResult.Unreachable);
        var bus = new Mock<IMessageBus>();
        var act = () => UpdateRoasterHandler.Handle(new UpdateRoasterCommand(roaster.Id, "Changed",
                ActorUserId: Guid.NewGuid(), Photos: [new("client", "image/jpeg", "new-key", 1)]),
            _repo.Object, _mapper.Object, _photos.Object, bus.Object, _ct);

        await act.Should().ThrowAsync<ServiceUnavailableException>();
        roaster.Name.Should().Be("Original");
        roaster.Photos.Should().ContainSingle(p => ReferenceEquals(p, old));
        bus.VerifyNoOtherCalls();
    }
}
