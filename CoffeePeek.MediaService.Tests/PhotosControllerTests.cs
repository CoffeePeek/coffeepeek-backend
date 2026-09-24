using CoffeePeek.Contract.Dtos;
using CoffeePeek.MediaService.Commands;
using CoffeePeek.MediaService.Commands.Base;
using CoffeePeek.MediaService.Controllers;
using CoffeePeek.MediaService.Responses;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Kernel.Response;
using FluentAssertions;
using Moq;
using Wolverine;

namespace CoffeePeek.MediaService.Tests;

public class PhotosControllerTests
{
    private readonly Mock<IMessageBus> _busMock = new();
    private readonly Mock<IUserContext> _userContextMock = new();
    private readonly Guid _userId = Guid.NewGuid();

    public PhotosControllerTests()
    {
        _userContextMock.Setup(u => u.GetUserIdOrThrow()).Returns(_userId);
    }

    private PhotosController CreateController() => new(_busMock.Object, _userContextMock.Object);

    [Fact]
    public async Task GenerateReviewUploadUrls_UsesCallerAsOwnerAndReviewsPrefix()
    {
        GenerateShopPhotosCommand? captured = null;
        _busMock.Setup(b => b.InvokeAsync<Response<List<GenerateUploadUrlResponse>>>(
                It.IsAny<object>(), It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()))
            .Callback<object, CancellationToken, TimeSpan?>((m, _, _) => captured = (GenerateShopPhotosCommand)m)
            .ReturnsAsync(Response<List<GenerateUploadUrlResponse>>.Success([]));

        await CreateController().GenerateReviewUploadUrls(
            [new PhotoRequest(1024, "a.jpg", "image/jpeg")], CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.OwnerId.Should().Be(_userId);
        captured.StorageKeyPrefix.Should().Be("reviews");
        captured.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task GenerateUploadUrls_Shop_StillUsesNullPrefix()
    {
        GenerateShopPhotosCommand? captured = null;
        _busMock.Setup(b => b.InvokeAsync<Response<List<GenerateUploadUrlResponse>>>(
                It.IsAny<object>(), It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()))
            .Callback<object, CancellationToken, TimeSpan?>((m, _, _) => captured = (GenerateShopPhotosCommand)m)
            .ReturnsAsync(Response<List<GenerateUploadUrlResponse>>.Success([]));

        await CreateController().GenerateUploadUrls(
            [new PhotoRequest(1024, "a.jpg", "image/jpeg")], CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.OwnerId.Should().Be(_userId);
        captured.StorageKeyPrefix.Should().BeNull();
    }

    [Fact]
    public async Task ResolveUploadedPhotos_OverridesOwnerFromPrincipal()
    {
        ResolveUploadedPhotosQuery? captured = null;
        _busMock.Setup(b => b.InvokeAsync<Response<List<ResolvedUploadedPhotoDto>>>(
                It.IsAny<object>(), It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()))
            .Callback<object, CancellationToken, TimeSpan?>((m, _, _) => captured = (ResolveUploadedPhotosQuery)m)
            .ReturnsAsync(Response<List<ResolvedUploadedPhotoDto>>.Success([]));

        var forged = new ResolveUploadedPhotosQuery(["reviews/a.jpg"]) { OwnerId = Guid.NewGuid() };
        await CreateController().ResolveUploadedPhotos(forged, CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.OwnerId.Should().Be(_userId);
        captured.StorageKeys.Should().Equal("reviews/a.jpg");
    }
}
