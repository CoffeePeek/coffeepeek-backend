using CoffeePeek.Contract.Dtos.CheckIns;
using CoffeePeek.Contract.Enums;
using CoffeePeek.MediaService.Services.Backfill;
using CoffeePeek.MediaService.Services.Variants;

namespace CoffeePeek.MediaService.Tests;

public sealed class PhotoBackfillRunTests
{
    [Fact]
    public void CutoffsAndRateBoundsCannotBeChangedIntoAnUnboundedScan()
    {
        var now = DateTime.UtcNow;
        var valid = new CreatePhotoBackfillRun(now, now.AddDays(-30));
        PhotoBackfillRuns.ValidateCreate(valid, now);
        foreach (var invalid in new[]
        {
            valid with { BatchSize = 0 }, valid with { BatchSize = 101 },
            valid with { MinBatchIntervalSeconds = 0 }, valid with { MinBatchIntervalSeconds = 3601 },
            valid with { BeforeUtc = now.AddDays(1) }, valid with { RecentSinceUtc = now },
            valid with { BeforeUtc = DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc) },
            valid with { RecentSinceUtc = DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc) },
            valid with { BeforeUtc = DateTime.SpecifyKind(now, DateTimeKind.Unspecified) }
        }) Assert.Throws<ArgumentException>(() => PhotoBackfillRuns.ValidateCreate(invalid, now));
    }

    [Fact]
    public void PriorityPhasesFinishWithoutCyclingOrReinterpretingTheCursor()
    {
        Assert.Equal(PhotoBackfillPhase.OtherShop, PhotoBackfillRuns.NextPhase(PhotoBackfillPhase.RecentPublicShop));
        Assert.Equal(PhotoBackfillPhase.Home, PhotoBackfillRuns.NextPhase(PhotoBackfillPhase.OtherShop));
        Assert.Equal(PhotoBackfillPhase.PublicShopArchive, PhotoBackfillRuns.NextPhase(PhotoBackfillPhase.Home));
        Assert.Null(PhotoBackfillRuns.NextPhase(PhotoBackfillPhase.PublicShopArchive));
    }

    [Fact]
    public void MalformedOrRepeatedCandidatePageCannotAdvanceCheckpoint()
    {
        var now = DateTime.UtcNow;
        var first = Candidate(now.AddHours(-1));
        var second = Candidate(now.AddHours(-2));
        var cursor = new PhotoBackfillCursor(first.SortAtUtc, first.CheckInId, first.PhotoId);
        var next = new PhotoBackfillCursor(second.SortAtUtc, second.CheckInId, second.PhotoId);
        PhotoBackfillRuns.ValidatePage(new([second], next), cursor, 1, now);
        Assert.Throws<InvalidDataException>(() => PhotoBackfillRuns.ValidatePage(new([first], cursor), cursor, 1, now));
        Assert.Throws<InvalidDataException>(() => PhotoBackfillRuns.ValidatePage(new([second], cursor), cursor, 1, now));
        Assert.Throws<InvalidDataException>(() => PhotoBackfillRuns.ValidatePage(new([second, second], next), cursor, 2, now));
        Assert.Throws<InvalidDataException>(() => PhotoBackfillRuns.ValidatePage(new([], next), cursor, 1, now));
        Assert.Throws<InvalidDataException>(() => PhotoBackfillRuns.ValidatePage(new([second, first], cursor), null, 2, now));
        Assert.Throws<InvalidDataException>(() => PhotoBackfillRuns.ValidatePage(new([second], next), null, 0, now));
    }

    private static PhotoBackfillCandidate Candidate(DateTime sortAt)
    {
        var id = Guid.NewGuid();
        var key = $"check-ins/{id:N}.jpg";
        return new(id, Guid.NewGuid(), Guid.NewGuid(), key, PhotoVariantRecipe.SourceVersion(id, key),
            CheckInKind.Shop, false, sortAt, 123, "image/jpeg");
    }
}
