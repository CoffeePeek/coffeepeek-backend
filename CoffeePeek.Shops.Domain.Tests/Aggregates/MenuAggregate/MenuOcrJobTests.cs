using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Domain.Aggregates.MenuAggregate;
using FluentAssertions;

namespace CoffeePeek.Shops.Domain.Tests.Aggregates.MenuAggregate;

public sealed class MenuOcrJobTests
{
    [Fact]
    public void ManualEdit_InvalidatesEntireEarlierJobIncludingNewDrinks()
    {
        var menu = ShopMenu.Create(Guid.NewGuid());
        var photo = ShopMenuPhoto.Create("menu.jpg", "image/jpeg", "menus/first", 100);
        menu.AddPhotos([photo]);
        var job = menu.QueueOcrJob([photo], DateTime.UtcNow);
        job.Matches(menu).Should().BeTrue();

        menu.ApplyManualItem(Guid.NewGuid(), MenuItemAvailability.Present, 5m, Guid.NewGuid());

        job.Matches(menu).Should().BeFalse();
        menu.InputRevision.Should().BeGreaterThan(job.MenuRevision!.Value);
    }

    [Fact]
    public void PhotoSetChange_AndNewerJob_InvalidateEarlierJob()
    {
        var menu = ShopMenu.Create(Guid.NewGuid());
        var firstPhoto = ShopMenuPhoto.Create("first.jpg", "image/jpeg", "menus/first", 100);
        menu.AddPhotos([firstPhoto]);
        var firstJob = menu.QueueOcrJob([firstPhoto], DateTime.UtcNow);
        var secondPhoto = ShopMenuPhoto.Create("second.jpg", "image/jpeg", "menus/second", 100);
        menu.AddPhotos([secondPhoto]);
        firstJob.Matches(menu).Should().BeFalse();

        var secondJob = menu.QueueOcrJob([secondPhoto], DateTime.UtcNow);
        secondJob.Matches(menu).Should().BeTrue();
        firstJob.Matches(menu).Should().BeFalse();

        menu.RemovePhotos([firstPhoto.Id]).IsSuccess.Should().BeTrue();
        secondJob.Matches(menu).Should().BeFalse();
    }

    [Fact]
    public void NewerJobWithSameInputs_StillInvalidatesEarlierJob()
    {
        var menu = ShopMenu.Create(Guid.NewGuid());
        var photo = ShopMenuPhoto.Create("menu.jpg", "image/jpeg", "menus/first", 100);
        menu.AddPhotos([photo]);
        var first = menu.QueueOcrJob([photo], DateTime.UtcNow);
        var second = menu.QueueOcrJob([photo], DateTime.UtcNow);

        first.Matches(menu).Should().BeFalse();
        second.Matches(menu).Should().BeTrue();
        second.PhotoSetHash.Should().Be(first.PhotoSetHash);
        second.MenuRevision.Should().Be(first.MenuRevision);
    }

    [Fact]
    public void ResultMustBeStoredBeforeApplied_AndCannotBeReplaced()
    {
        var menu = ShopMenu.Create(Guid.NewGuid());
        var photo = ShopMenuPhoto.Create("menu.jpg", "image/jpeg", "menus/first", 100);
        menu.AddPhotos([photo]);
        var job = menu.QueueOcrJob([photo], DateTime.UtcNow);
        var now = DateTime.UtcNow;

        var applyWithoutResult = () => job.MarkApplied(now);
        applyWithoutResult.Should().Throw<DomainException>();
        job.Start(now);
        job.StoreResult("{\"items\":[]}", now);
        job.Status.Should().Be(MenuOcrJobStatus.ResultStored);
        job.ResultJson.Should().Be("{\"items\":[]}");
        var replaceResult = () => job.StoreResult("{\"items\":[1]}", now);
        replaceResult.Should().Throw<DomainException>();
        job.MarkApplied(now);
        job.Status.Should().Be(MenuOcrJobStatus.Applied);
    }

    [Fact]
    public void PhotoHash_IsOrderIndependentButDetectsChangedStorageKey()
    {
        var first = ShopMenuPhoto.Create("first.jpg", "image/jpeg", "menus/first", 100);
        var second = ShopMenuPhoto.Create("second.jpg", "image/jpeg", "menus/second", 200);
        MenuOcrPhotoSet.Hash([first, second]).Should().Be(MenuOcrPhotoSet.Hash([second, first]));
        MenuOcrPhotoSet.Hash([first]).Should().NotBe(MenuOcrPhotoSet.Hash([second]));
    }

    [Fact]
    public void UnknownProviderOutcome_IsTerminalAndCannotAcceptLateResult()
    {
        var menu = ShopMenu.Create(Guid.NewGuid());
        var photo = ShopMenuPhoto.Create("menu.jpg", "image/jpeg", "menus/first", 100);
        menu.AddPhotos([photo]);
        var job = menu.QueueOcrJob([photo], DateTime.UtcNow);
        var started = DateTime.UtcNow;

        job.Start(started);
        job.MarkProviderOutcomeUnknown(started.AddMinutes(16));

        job.Status.Should().Be(MenuOcrJobStatus.Failed);
        job.Error.Should().Be(MenuOcrJob.ProviderOutcomeUnknownError);
        job.CompletedAtUtc.Should().Be(started.AddMinutes(16));
        job.ResultJson.Should().BeNull();
        var lateResult = () => job.StoreResult("{\"items\":[]}", started.AddMinutes(17));
        lateResult.Should().Throw<DomainException>();
    }
}
