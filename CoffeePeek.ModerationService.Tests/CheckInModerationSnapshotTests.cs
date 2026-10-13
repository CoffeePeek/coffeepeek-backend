using System.Reflection;
using CoffeePeek.Contract.Dtos;
using CoffeePeek.Contract.Dtos.CheckIns;
using CoffeePeek.Contract.Dtos.CoffeeShop;
using CoffeePeek.Contract.Events.Shops;
using CoffeePeek.Moderation.Application.Mapper;
using CoffeePeek.Moderation.Domain.Aggregates.ModerationReviewAggregate;
using CoffeePeek.Moderation.Domain.Entities;
using CoffeePeek.Moderation.Infrastructure.Consumers;
using CoffeePeek.ModerationService.Controllers;
using CoffeePeek.Shared.Auth.Constants;
using CoffeePeek.Shared.Kernel;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Options;
using CoffeeShop.Moderation.Persistence.Configuration;
using CoffeeShop.Moderation.Persistence.Repositories;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;

namespace CoffeePeek.ModerationService.Tests;

public class CheckInModerationSnapshotTests
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task HomeSnapshotPersistsRecipeWithoutShopAndModeratesCustomBeanText()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = new ModerationDbContext(new DbContextOptionsBuilder<ModerationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var request = Request(Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid()) with
        {
            ShopId = null, Text = "", Rating = null, Photos = [],
            Home = new HomeCheckInDetailsDto { PreparedOn = new DateOnly(2026, 9, 20),
                BeanName = "User-supplied beans", BrewMethodCode = "aeropress", DoseGrams = 18m,
                BrewDurationSeconds = 120, TasteScore = 5 }
        };
        var (decision, _) = await CheckInModerationRequestedHandler.Handle(request,
            new ModerationReviewRepository(db), new TestUnitOfWork(db), ct);
        Assert.Null(decision);
        db.ChangeTracker.Clear();
        var snapshot = await db.ModerationReviews.SingleAsync(ct);
        Assert.Null(snapshot.ShopId);
        Assert.Equal(request.Home.BeanName, snapshot.Home!.BeanName);
        Assert.Equal(CoffeePeek.Moderation.Domain.Common.Enums.ModerationStatus.Pending, snapshot.ModerationStatus);
        var mapper = MapsterConfiguration.CreateMapper(new MediaPublicUrlOptions { PublicEndpoint = "https://media.example.com" });
        var dto = mapper.Map<ModerationReviewDto>(snapshot);
        Assert.Equal(CoffeePeek.Contract.Enums.CheckInKind.Home, dto.Kind);
        Assert.Equal(request.Home, dto.Home);
    }

    [Fact]
    public async Task DateOnlySnapshotCanBePersistedAndMappedWithoutRating()
    {
        await using var db = new ModerationDbContext(new DbContextOptionsBuilder<ModerationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var request = Request(Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid()) with { Text = "", Rating = null, Photos = [] };
        var (decision, notification) = await CheckInModerationRequestedHandler.Handle(request,
            new ModerationReviewRepository(db), new TestUnitOfWork(db), _ct);
        db.ChangeTracker.Clear();
        var snapshot = await db.ModerationReviews.SingleAsync(_ct);
        var mapper = MapsterConfiguration.CreateMapper(new MediaPublicUrlOptions { PublicEndpoint = "https://media.example.com" });
        var dto = mapper.Map<ModerationReviewDto>(snapshot);
        Assert.Equal(request.VisitedAtUtc, dto.VisitedAtUtc);
        Assert.Equal("", dto.Text);
        Assert.Null(dto.Rating);
        snapshot.ModerationStatus.Should().Be(CoffeePeek.Moderation.Domain.Common.Enums.ModerationStatus.Approved);
        decision.Should().NotBeNull();
        decision!.CheckInId.Should().Be(request.CheckInId);
        decision.ContentRevision.Should().Be(request.ContentRevision);
        decision.SubmissionId.Should().Be(snapshot.Id);
        decision.DecisionSequence.Should().Be(1);
        decision.Status.Should().Be(CoffeePeek.Contract.Enums.ModerationStatus.Approved);
        decision.DecidedAtUtc.Should().Be(snapshot.ModeratedAt);
        notification!.Details.Should().Contain("Одобрен автоматически");

        var duplicate = await CheckInModerationRequestedHandler.Handle(request,
            new ModerationReviewRepository(db), new TestUnitOfWork(db), _ct);
        duplicate.Item1.Should().BeNull();
        duplicate.Item2.Should().BeNull();
    }

    private static CheckInModerationRequestedEvent Request(Guid id, int revision, Guid owner, Guid photoId) =>
        new(id, revision, owner, "alice", Guid.NewGuid(), "Y", new RatingDto { Coffee = 4, Service = 5, Place = 3 },
            null, null, null, null, DateTime.UtcNow, [new CheckInPhotoDto(photoId, "photo.jpg", "image/jpeg", "private-key", 32, 0, "")]);

    [Fact]
    public async Task SnapshotRequests_AreIdempotent_AndPreserveCanonicalPhotoIdentityAcrossRevisions()
    {
        await using var db = new ModerationDbContext(new DbContextOptionsBuilder<ModerationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var repository = new ModerationReviewRepository(db);
        var unitOfWork = new TestUnitOfWork(db);
        var request = Request(Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid());

        var (decision, firstNotification) = await CheckInModerationRequestedHandler.Handle(request, repository, unitOfWork, _ct);
        var duplicate = await CheckInModerationRequestedHandler.Handle(request, repository, unitOfWork, _ct);
        var (revisedDecision, revisedNotification) = await CheckInModerationRequestedHandler.Handle(request with { ContentRevision = 2, Text = "Second" },
            repository, unitOfWork, _ct);
        var snapshots = await db.ModerationReviews.Include(r => r.ReviewPhotos).ToArrayAsync(_ct);

        snapshots.Should().HaveCount(2);
        snapshots.SelectMany(r => r.ReviewPhotos).Select(p => p.CheckInPhotoId).Should()
            .OnlyContain(id => id == request.Photos[0].Id);
        snapshots.SelectMany(r => r.ReviewPhotos).Select(p => p.Id).Distinct().Should().HaveCount(2);
        unitOfWork.Saves.Should().Be(2);
        firstNotification!.Kind.Should().Be(CoffeePeek.Contract.Events.Moderation.ModerationSubmissionKind.CheckIn);
        decision.Should().BeNull();
        revisedDecision.Should().BeNull();
        duplicate.Item1.Should().BeNull();
        duplicate.Item2.Should().BeNull();
        revisedNotification!.SubmissionId.Should().NotBe(firstNotification.SubmissionId);

        Func<Task> conflict = () => CheckInModerationRequestedHandler.Handle(request with { UserId = Guid.NewGuid() },
            repository, unitOfWork, _ct);
        await conflict.Should().ThrowAsync<DomainException>();
    }

    [Theory]
    [InlineData("latte", null, "Латте", "Latte")]
    [InlineData("other", "Эспрессо-тоник", "Эспрессо-тоник", "Эспрессо-тоник")]
    public async Task SnapshotRequest_FreezesAllDrinkFields_AndLaterRevisionCanClearDrink(
        string slug, string? customName, string nameRu, string nameEn)
    {
        await using var db = new ModerationDbContext(new DbContextOptionsBuilder<ModerationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var repository = new ModerationReviewRepository(db);
        var request = Request(Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid()) with
        {
            DrinkSlug = slug, CustomDrinkName = customName, DrinkNameRu = nameRu, DrinkNameEn = nameEn
        };
        await CheckInModerationRequestedHandler.Handle(request, repository, new TestUnitOfWork(db), _ct);
        await CheckInModerationRequestedHandler.Handle(request with
        {
            ContentRevision = 2, DrinkSlug = null, CustomDrinkName = null, DrinkNameRu = null, DrinkNameEn = null
        }, repository, new TestUnitOfWork(db), _ct);
        db.ChangeTracker.Clear();

        var snapshots = await db.ModerationReviews.OrderBy(x => x.ContentRevision).ToArrayAsync(_ct);
        var mapper = MapsterConfiguration.CreateMapper(new MediaPublicUrlOptions { PublicEndpoint = "https://media.example.com" });
        var original = mapper.Map<ModerationReviewDto>(snapshots[0]);
        original.DrinkSlug.Should().Be(slug);
        original.CustomDrinkName.Should().Be(customName);
        original.DrinkNameRu.Should().Be(nameRu);
        original.DrinkNameEn.Should().Be(nameEn);
        snapshots[1].DrinkSlug.Should().BeNull();
        snapshots[1].DrinkNameRu.Should().BeNull();
    }

    [Fact]
    public async Task SnapshotRequest_RejectsDrinkFieldsWithoutSlugBeforeSaving()
    {
        await using var db = new ModerationDbContext(new DbContextOptionsBuilder<ModerationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var unitOfWork = new TestUnitOfWork(db);
        var request = Request(Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid()) with { DrinkNameRu = "Латте" };
        var act = () => CheckInModerationRequestedHandler.Handle(request, new ModerationReviewRepository(db), unitOfWork, _ct);
        await act.Should().ThrowAsync<DomainException>();
        unitOfWork.Saves.Should().Be(0);
        db.ModerationReviews.Should().BeEmpty();
    }

    [Fact]
    public async Task NewRevisionWithText_AfterAutoApprovalRequiresModeration()
    {
        await using var db = new ModerationDbContext(new DbContextOptionsBuilder<ModerationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var repository = new ModerationReviewRepository(db);
        var unitOfWork = new TestUnitOfWork(db);
        var request = Request(Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid()) with { Text = "", Photos = [] };
        var first = await CheckInModerationRequestedHandler.Handle(request, repository, unitOfWork, _ct);
        var edited = await CheckInModerationRequestedHandler.Handle(request with { ContentRevision = 2, Text = "New text" },
            repository, unitOfWork, _ct);

        first.Item1.Should().NotBeNull();
        edited.Item1.Should().BeNull();
        edited.Item2!.Details.Should().Be("New text");
        var snapshots = await db.ModerationReviews.OrderBy(r => r.ContentRevision).ToArrayAsync(_ct);
        snapshots[0].ModerationStatus.Should().Be(CoffeePeek.Moderation.Domain.Common.Enums.ModerationStatus.Approved);
        snapshots[1].ModerationStatus.Should().Be(CoffeePeek.Moderation.Domain.Common.Enums.ModerationStatus.Pending);
    }

    [Fact]
    public void Persistence_RequiresCanonicalIdentityRevisionVisit_AndXminConcurrency()
    {
        using var db = new ModerationDbContext(new DbContextOptionsBuilder<ModerationDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=test;Password=test").Options);
        var entity = db.Model.FindEntityType(typeof(ModerationReview))!;
        entity.FindProperty("Version")!.IsConcurrencyToken.Should().BeTrue();
        foreach (var property in new[] { "CheckInId", "ContentRevision", "VisitedAtUtc" })
            entity.FindProperty(property)!.IsNullable.Should().BeFalse();
        entity.GetIndexes().Should().Contain(i => i.IsUnique &&
            i.Properties.Select(p => p.Name).SequenceEqual(new[] { "CheckInId", "ContentRevision" }));
        foreach (var property in new[] { "LegacySourceId", "Header", "ModerationShopId" })
            entity.FindProperty(property).Should().BeNull();
    }

    [Fact]
    public void SnapshotPhotos_MapOnlyToCanonicalRevisionCheckedGatewayRoute()
    {
        var photoId = Guid.NewGuid();
        var checkInId = Guid.NewGuid();
        var author = Guid.NewGuid();
        var snapshot = ModerationReview.CreateCheckInSnapshot(checkInId, 7, author, Guid.NewGuid(), "alice",
            "Y", 3, 3, 3, null, DateTime.UtcNow,
            [PhotoMetadata.CreateCheckInPhoto(photoId, 0, "a.jpg", "image/jpeg", "private-key", 32, author)]);
        var mapper = MapsterConfiguration.CreateMapper(new MediaPublicUrlOptions { PublicEndpoint = "https://media.example.com" });
        var dto = mapper.Map<ModerationReviewDto>(snapshot);
        dto.CheckInId.Should().Be(checkInId);
        dto.ContentRevision.Should().Be(7);
        dto.Text.Should().Be("Y");
        dto.Photos.Single().Id.Should().Be(photoId);
        dto.Photos.Single().FullUrl.Should().Be($"/api/v1/check-ins/{checkInId}/moderation-photos/{photoId}?revision=7");
    }

    [Fact]
    public void ModeratorApi_HasOnlyQueueDetailAndStatusActions()
    {
        var controller = typeof(ModerationReviewsController);
        controller.GetCustomAttribute<RouteAttribute>()!.Template.Should().Be("api/v1/moderation/check-ins");
        controller.GetCustomAttribute<AuthorizeAttribute>()!.Policy.Should().Be(RoleConsts.Moderator);
        var methods = controller.GetMethods().Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any()).ToArray();
        methods.Should().HaveCount(3);
        methods.SelectMany(m => m.GetCustomAttributes<HttpMethodAttribute>()).SelectMany(a => a.HttpMethods)
            .Should().BeEquivalentTo(["GET", "GET", "PUT"]);
        methods.SelectMany(m => m.GetCustomAttributes<HttpMethodAttribute>()).Select(a => a.Template)
            .Should().BeEquivalentTo([null, "{id:guid}", null]);
        controller.Assembly.GetType("CoffeePeek.ModerationService.Controllers.InternalModerationReviewsController")
            .Should().BeNull();
    }

    private sealed class TestUnitOfWork(ModerationDbContext db) : IUnitOfWork
    {
        public int Saves { get; private set; }
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            Saves++;
            return db.SaveChangesAsync(cancellationToken);
        }
        public Task ExecuteAsync(Func<Task> operation, CancellationToken ct = default) => operation();
        public void ClearTracking() => db.ChangeTracker.Clear();
    }
}
