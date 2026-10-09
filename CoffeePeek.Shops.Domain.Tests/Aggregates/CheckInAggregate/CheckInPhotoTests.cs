using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shops.Domain.Aggregates.CheckInAggregate;
using CoffeePeek.Shops.Domain.Entities;

namespace CoffeePeek.Shops.Domain.Tests.Aggregates.CheckInAggregate;

public sealed class CheckInPhotoTests
{
    private static CheckIn Visit(Guid owner) => CheckIn.CreateUnified(owner, Guid.NewGuid(), "author", "coffee",
        Rating.Create(4, 4, 4), DateTime.UtcNow, CheckInVisibility.Public);
    private static CheckInPhoto Photo(Guid owner, string key) => new("coffee.jpg", "image/jpeg", key, 42, owner);

    [Theory]
    [InlineData("foreign")]
    [InlineData("duplicate")]
    [InlineData("limit")]
    [InlineData("shared")]
    public void InvalidBatchLeavesCollectionRevisionAndEveryPhotoUnchanged(string scenario)
    {
        var owner = Guid.NewGuid();
        var visit = Visit(owner);
        var first = Photo(owner, "first");
        CheckInPhoto[] photos = scenario switch
        {
            "foreign" => [first, Photo(Guid.NewGuid(), "foreign")],
            "duplicate" => [first, Photo(owner, "first")],
            "limit" => Enumerable.Range(0, 6).Select(i => Photo(owner, "photo-" + i)).ToArray(),
            _ => [first, Shared()]
        };
        var revision = visit.ContentRevision;
        var version = visit.RowVersion;
        var originalParents = photos.Select(p => p.CanonicalCheckInId).ToArray();
        Assert.Throws<DomainException>(() => visit.AddPhotos(photos));
        Assert.Empty(visit.ShopPhotos);
        Assert.Equal(revision, visit.ContentRevision);
        Assert.Equal(version, visit.RowVersion);
        Assert.Equal(originalParents, photos.Select(p => p.CanonicalCheckInId).ToArray());

        CheckInPhoto Shared()
        {
            var shared = Photo(owner, "shared");
            Visit(owner).AddPhotos([shared]);
            return shared;
        }
    }

    [Fact]
    public void AddingPhotosInvalidatesApprovalAndRejectsItsDelayedDecision()
    {
        var visit = Visit(Guid.NewGuid());
        var submission = Guid.NewGuid();
        Assert.True(visit.ApplyModerationDecision(1, submission, 1, CheckInModerationState.Approved, null, DateTime.UtcNow));
        var version = visit.RowVersion;
        var photo = Photo(visit.UserId, "private-object");
        visit.AddPhotos([photo]);
        Assert.Equal(visit.Id, photo.CanonicalCheckInId);
        Assert.Equal(2, visit.ContentRevision);
        Assert.NotEqual(version, visit.RowVersion);
        Assert.False(visit.IsPubliclyVisible);
        Assert.Null(visit.ApprovedRevision);
        Assert.Null(visit.ModerationSubmissionId);
        Assert.False(visit.ApplyModerationDecision(1, submission, 2, CheckInModerationState.Approved, null, DateTime.UtcNow));
        Assert.Throws<NotSupportedException>(() => ((ICollection<CheckInPhoto>)visit.ShopPhotos).Clear());
        Assert.All(typeof(CheckInPhoto).GetProperties().Where(p => p.DeclaringType == typeof(CheckInPhoto)),
            p => Assert.False(p.SetMethod?.IsPublic == true));
    }

    [Fact]
    public void EmptyBatchIsNoOpAndDeletedVisitCannotAttachPhotos()
    {
        var visit = Visit(Guid.NewGuid());
        var version = visit.RowVersion;
        visit.AddPhotos([]);
        Assert.Equal(1, visit.ContentRevision);
        Assert.Equal(version, visit.RowVersion);
        visit.Delete();
        Assert.Throws<DomainException>(() => visit.AddPhotos([Photo(visit.UserId, "object")]));
    }
}
