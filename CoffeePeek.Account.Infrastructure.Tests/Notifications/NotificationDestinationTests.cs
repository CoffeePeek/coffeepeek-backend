using CoffeePeek.Account.Application.Features.Notifications;
using CoffeePeek.Contract.Dtos.Notifications;
using CoffeePeek.Contract.Dtos.PublicAddresses;
using CoffeePeek.Shared.Kernel.Exceptions;
using Moq;
using Xunit;

namespace CoffeePeek.Account.Infrastructure.Tests.Notifications;

public sealed class NotificationDestinationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly Guid userId = Guid.NewGuid();
    private readonly Guid notificationId = Guid.NewGuid();
    private readonly Guid entityId = Guid.NewGuid();
    private readonly Mock<IPushNotificationStore> store = new();
    private readonly Mock<INotificationShopAddressLookup> shops = new();

    [Fact]
    public async Task OldPublicationUsesCurrentSlugAndNeverChangesReadState()
    {
        Item("NewShop", "Shop");
        shops.SetupSequence(x => x.FindAsync(entityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PublicAddressMetadata(entityId, "old-name", "/coffee-shops/old-name", 1))
            .ReturnsAsync(new PublicAddressMetadata(entityId, "current-name", "/coffee-shops/current-name", 2));
        var resolver = new NotificationDestinationResolver(store.Object, shops.Object);
        Assert.Equal(new NotificationDestinationDto("Shop", "old-name"), await resolver.ResolveAsync(userId, notificationId, Ct));
        Assert.Equal(new NotificationDestinationDto("Shop", "current-name"), await resolver.ResolveAsync(userId, notificationId, Ct));
        store.Verify(x => x.MarkReadAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("CorrectionModeration", "ShopChange", "ShopChange")]
    [InlineData("ShopApproved", "Shop", "ShopSubmission")]
    [InlineData("ShopRejected", "Shop", "ShopSubmission")]
    [InlineData("RoasterApproved", "Roaster", "RoasterSubmission")]
    [InlineData("CheckInModeration", "CheckIn", "CheckIns")]
    public async Task SubmissionIdsAreNotMistakenForPublishedShopIds(string type, string entity, string expected)
    {
        Item(type, entity);
        var destination = await new NotificationDestinationResolver(store.Object, shops.Object)
            .ResolveAsync(userId, notificationId, Ct);
        Assert.Equal(new NotificationDestinationDto(expected, entityId.ToString("D")), destination);
        shops.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HiddenShopDoesNotProduceAnObsoleteAddress()
    {
        Item("NewShop", "Shop");
        await Assert.ThrowsAsync<NotFoundException>(() => new NotificationDestinationResolver(store.Object, shops.Object)
            .ResolveAsync(userId, notificationId, Ct));
    }

    [Fact]
    public async Task ForeignNotificationIsRejectedBeforeLookingUpAnyEntity()
    {
        store.Setup(x => x.GetNotificationAsync(userId, notificationId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotFoundException("Notification not found."));
        await Assert.ThrowsAsync<NotFoundException>(() => new NotificationDestinationResolver(store.Object, shops.Object)
            .ResolveAsync(userId, notificationId, Ct));
        shops.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("General", "Shop")]
    [InlineData("NewShop", "ShopChange")]
    public async Task UnknownOrMismatchedMetadataHasNoDestination(string type, string entity)
    {
        Item(type, entity);
        Assert.Null(await new NotificationDestinationResolver(store.Object, shops.Object)
            .ResolveAsync(userId, notificationId, Ct));
        shops.VerifyNoOtherCalls();
    }

    private void Item(string type, string entity) => store.Setup(x =>
        x.GetNotificationAsync(userId, notificationId, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new NotificationDto(notificationId, type, "Title", "Body", DateTime.UtcNow, null, entity, entityId, "Approved"));
}
