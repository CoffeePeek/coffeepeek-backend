using CoffeePeek.Contract.Dtos;
using CoffeePeek.MediaService.Commands;
using CoffeePeek.MediaService.Commands.Base;
using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Controllers;
using CoffeePeek.MediaService.Responses;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Kernel.Response;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
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
    public void UploadRoutes_ExposeOnlyCanonicalCheckInAction()
    {
        var routes = typeof(PhotosController).GetMethods()
            .SelectMany(method => method.GetCustomAttributes(typeof(HttpPostAttribute), false)
                .Cast<HttpPostAttribute>()).Select(route => route.Template).ToArray();

        routes.Should().Contain("check-in");
        routes.Should().NotContain("review");
    }

    [Fact]
    public async Task GenerateCheckInUploadUrls_UsesCallerAndPrivateCheckInBucket()
    {
        GenerateShopPhotosCommand? captured = null;
        _busMock.Setup(b => b.InvokeAsync<Response<List<GenerateUploadUrlResponse>>>(
                It.IsAny<object>(), It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()))
            .Callback<object, CancellationToken, TimeSpan?>((m, _, _) => captured = (GenerateShopPhotosCommand)m)
            .ReturnsAsync(Response<List<GenerateUploadUrlResponse>>.Success([]));

        await CreateController().GenerateCheckInUploadUrls(
            [new PhotoRequest(1024, "a.jpg", "image/jpeg")], CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.OwnerId.Should().Be(_userId);
        captured.StorageKeyPrefix.Should().Be("check-ins");
        captured.Bucket.Should().Be(BucketType.CheckIn);
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

        var forged = new ResolveUploadedPhotosQuery(["check-ins/a.jpg"]) { OwnerId = Guid.NewGuid() };
        await CreateController().ResolveUploadedPhotos(forged, CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.OwnerId.Should().Be(_userId);
        captured.StorageKeys.Should().Equal("check-ins/a.jpg");
    }
}
