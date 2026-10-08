using System;
using System.Net;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Dtos.Shop;
using CoffeePeek.Shared.Domain.Interfaces.Infrastructure;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shops.Application.Features.Admin.Catalogs.Roasters;
using CoffeePeek.Shops.Application.Abstractions;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Domain.Aggregates.CoffeeShopAggregate;
using FluentAssertions;
using MapsterMapper;
using Moq;

namespace CoffeePeek.Shops.Application.Tests.Features.Admin.Catalogs.Roasters;

public class CreateRoasterHandlerTests
{
    private readonly Mock<IRoasterRepository> _repo = new();
    private readonly Mock<IMapper> _mapper = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<ICacheService> _cache = new();
    private readonly Mock<IUploadedPhotoLookup> _photos = new();
    private readonly CancellationToken _ct = CancellationToken.None;

    [Fact]
    public async Task Handle_NewName_CreatesRoasterAndInvalidatesCache()
    {
        _repo.Setup(r => r.GetByNameAsync("Coffee Circus", _ct)).ReturnsAsync((Roaster)null);
        _mapper.Setup(m => m.Map<RoasterDto>(It.IsAny<Roaster>()))
            .Returns((Roaster r) => new RoasterDto { Id = r.Id, Name = r.Name });
        _cache.Setup(c => c.RemoveByPattern(It.IsAny<string>(), _ct)).ReturnsAsync(1);

        var result = await CreateRoasterHandler.Handle(
            new CreateRoasterCommand("Coffee Circus"),
            _repo.Object, _mapper.Object, _uow.Object, _cache.Object, _photos.Object, _ct);

        result.IsSuccess.Should().BeTrue();
        result.Data.Name.Should().Be("Coffee Circus");
        _repo.Verify(r => r.Add(It.IsAny<Roaster>()), Times.Once);
        _uow.Verify(u => u.SaveChangesAsync(_ct), Times.Once);
        _cache.Verify(c => c.RemoveByPattern(CacheKey.Roaster.ListPattern(), _ct), Times.Once);
        _cache.Verify(c => c.RemoveByPattern(CacheKey.Shop.SearchPattern(), _ct), Times.Once);
    }

    [Fact]
    public async Task Handle_DuplicateName_ReturnsConflict()
    {
        var existing = new Roaster("Coffee Circus");
        _repo.Setup(r => r.GetByNameAsync("Coffee Circus", _ct)).ReturnsAsync(existing);

        var result = await CreateRoasterHandler.Handle(
            new CreateRoasterCommand("Coffee Circus"),
            _repo.Object, _mapper.Object, _uow.Object, _cache.Object, _photos.Object, _ct);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be((int)HttpStatusCode.Conflict);
        _repo.Verify(r => r.Add(It.IsAny<Roaster>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WithProfileFields_PersistsAboutLocationContactAndPhotos()
    {
        _repo.Setup(r => r.GetByNameAsync("Coffee Circus", _ct)).ReturnsAsync((Roaster)null);
        Roaster captured = null;
        _repo.Setup(r => r.Add(It.IsAny<Roaster>())).Callback<Roaster>(r => captured = r);
        _mapper.Setup(m => m.Map<RoasterDto>(It.IsAny<Roaster>()))
            .Returns((Roaster r) => new RoasterDto { Id = r.Id, Name = r.Name });
        _cache.Setup(c => c.RemoveByPattern(It.IsAny<string>(), _ct)).ReturnsAsync(1);

        var cityId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        _photos.Setup(p => p.ResolveAsync(actorId, It.IsAny<IReadOnlyCollection<string>>(), _ct))
            .ReturnsAsync(new UploadedPhotoLookupResult(true,
                [new ResolvedUploadedPhotoDto("key-a", "trusted.jpg", "image/jpeg", 150)]));
        var command = new CreateRoasterCommand(
            "Coffee Circus",
            About: "Small-batch roaster.",
            CityId: cityId,
            Address: "1 Main St",
            Latitude: 53.9m,
            Longitude: 27.5m,
            InstagramLink: "https://instagram.com/coffeecircus",
            SiteLink: "https://coffeecircus.by",
            Photos: [new UploadedPhotoDto("a.jpg", "image/jpeg", "key-a", 100)],
            ActorUserId: actorId);

        var result = await CreateRoasterHandler.Handle(
            command, _repo.Object, _mapper.Object, _uow.Object, _cache.Object, _photos.Object, _ct);

        result.IsSuccess.Should().BeTrue();
        captured.About.Should().Be("Small-batch roaster.");
        captured.Location!.Address.Should().Be("1 Main St");
        captured.Location.IsAddressValidated.Should().BeTrue();
        captured.Contact!.InstagramLink.Should().Be("https://instagram.com/coffeecircus");
        captured.Photos.Should().ContainSingle(p => p.StorageKey == "key-a");
        captured.Photos.Single().FileName.Should().Be("trusted.jpg");
        captured.Photos.Single().SizeBytes.Should().Be(150);
    }

    [Fact]
    public async Task Handle_ForeignPhoto_DoesNotCreateRoaster()
    {
        _repo.Setup(r => r.GetByNameAsync("Coffee Circus", _ct)).ReturnsAsync((Roaster)null);
        _photos.Setup(p => p.ResolveAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyCollection<string>>(), _ct))
            .ReturnsAsync(new UploadedPhotoLookupResult(true, []));
        var command = new CreateRoasterCommand("Coffee Circus",
            Photos: [new UploadedPhotoDto("a.jpg", "image/jpeg", "foreign-key", 10)],
            ActorUserId: Guid.NewGuid());

        var act = () => CreateRoasterHandler.Handle(command,
            _repo.Object, _mapper.Object, _uow.Object, _cache.Object, _photos.Object, _ct);

        await act.Should().ThrowAsync<ValidationException>();
        _repo.Verify(r => r.Add(It.IsAny<Roaster>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(_ct), Times.Never);
    }
}
