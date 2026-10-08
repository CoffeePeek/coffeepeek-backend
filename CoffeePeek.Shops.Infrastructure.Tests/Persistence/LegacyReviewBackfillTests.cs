using System.Text.Json.Nodes;
using CoffeePeek.LegacyCheckInsMigration;
namespace CoffeePeek.Shops.Infrastructure.Tests.Persistence;
public class LegacyReviewBackfillTests
{
    public static JsonObject Fixture()
    {
        var user = "11111111-1111-1111-1111-111111111111";
        var shop = "22222222-2222-2222-2222-222222222222";
        var inventory = new JsonObject { ["Users"] = new JsonArray(new JsonObject { ["Id"] = user, ["Username"] = "Author", ["CheckInCount"] = 9, ["AddedShopsCount"] = 7 }),
            ["Shops"] = new JsonArray(new JsonObject { ["Id"] = shop }), ["Drinks"] = new JsonArray(),
            ["Reviews"] = new JsonArray(), ["CheckIns"] = new JsonArray(), ["ModerationReviews"] = new JsonArray(),
            ["ReviewHelpfulVotes"] = new JsonArray(), ["ReviewReports"] = new JsonArray(),
            ["CanonicalCheckIns"] = new JsonArray(), ["ModerationCheckIns"] = new JsonArray() };
        for (var i = 1; i <= 5; i++)
        {
            var id = $"33333333-3333-3333-3333-{i:000000000000}";
            ((JsonArray)inventory["Reviews"]!).Add(new JsonObject {
                ["Id"] = id, ["UserId"] = user, ["CoffeeShopId"] = shop, ["UserName"] = "Author",
                ["Header"] = "retained header", ["Comment"] = "Legacy "+i, ["Rating_Place"] = 3, ["Rating_Service"] = 4,
                ["Rating_Coffee"] = 5, ["CreatedAtUtc"] = "2025-01-01T00:00:00Z", ["IsSoftDelete"] = i == 4,
                ["IsRemovedByAdmin"] = i == 5, ["HelpfulCount"] = 12 });
        }
        var reviews = (JsonArray)inventory["Reviews"]!;
        foreach (var i in new[]{2,3})
        {
            var mid = $"44444444-4444-4444-4444-{i:000000000000}";
            reviews[i-1]!["ModerationReviewId"] = mid;
            var m = (JsonObject)reviews[i-1]!.DeepClone(); m["Id"] = mid; m["ShopId"] = shop;
            m["ModerationStatus"] = i == 2 ? 0 : 2; m["IsSoftDelete"] = false;
            ((JsonArray)inventory["ModerationReviews"]!).Add(m);
        }
        return inventory;
    }
    [Fact]
    public void ApprovedUnlinkedReviewsRetainIdsAndEligibilityWhileLinkedPendingRejectedAndTombstonesStayHidden()
    {
        var inv = Fixture(); var original = inv.ToJsonString(); var plan = LegacyReviewBackfill.Map(inv);
        Assert.Equal(5, plan.Count(x => x!["Target"] is not null));
        var rows = plan.Where(x => x!["Target"] is not null).Select(x => x!["Target"]!).ToArray();
        Assert.Equal(inv["Reviews"]![0]!["Id"]!.ToString(), rows[0]["Id"]!.ToString());
        Assert.Equal(2, rows[0]["ModerationState"]!.GetValue<int>());
        Assert.Equal(1, rows[0]["Visibility"]!.GetValue<int>());
        Assert.Equal(4m, rows[0]["Rating_AverageRating"]!.GetValue<decimal>());
        Assert.Equal(1, rows[1]["ModerationState"]!.GetValue<int>());
        Assert.Equal(3, rows[2]["ModerationState"]!.GetValue<int>());
        Assert.NotNull(rows[3]["DeletedAtUtc"]); Assert.NotNull(rows[4]["DeletedAtUtc"]);
        Assert.Equal(original, inv.ToJsonString());
    }

    public static JsonObject Manifest(JsonObject? inventory = null)
    {
        inventory ??= Fixture();
        foreach (var key in new[]{"Photos","ShopPhotos","ModerationPhotos","Gallery"})
            inventory[key] ??= new JsonArray();
        var manifest = new JsonObject { ["Inventory"] = inventory, ["Mappings"] = LegacyReviewBackfill.Map(inventory),
            ["StartedAtUtc"] = "2026-10-07T00:00:00Z", ["CacheInvalidationPending"] = true };
        LegacyReviewBackfill.PrepareVotes(manifest); LegacyReviewBackfill.PreparePhotos(manifest); return manifest;
    }
    [Fact]
    public void ExplicitVisitLinkPreservesTimeAndUnlinkedValidSubmissionIsAuthorVisible()
    {
        var inv = Fixture();
        var visit = (JsonObject)inv["Reviews"]![0]!.DeepClone();
        visit["Id"] = Guid.NewGuid().ToString(); visit["ReviewId"] = inv["Reviews"]![0]!["Id"]!.DeepClone();
        visit["ShopId"] = visit["CoffeeShopId"]!.DeepClone(); visit["VisitedAt"] = "2024-12-30T00:00:00Z";
        visit["CreatedAtUtc"] = "2024-12-31T00:00:00Z"; inv["CheckIns"]!.AsArray().Add(visit);
        var submission = (JsonObject)inv["ModerationReviews"]![0]!.DeepClone();
        submission["Id"] = Guid.NewGuid().ToString(); inv["ModerationReviews"]!.AsArray().Add(submission);
        var plan = LegacyReviewBackfill.Map(inv);
        var mapped = plan[0]!["Target"]!;
        Assert.Equal("2024-12-30T00:00:00Z",mapped["VisitedAt"]!.ToString());
        Assert.Equal("2024-12-31T00:00:00Z",mapped["CreatedAtUtc"]!.ToString());
        Assert.Contains(plan,p => p!["SourceTable"]!.ToString() == "CheckIns" && p["Outcome"]!.ToString() == "merged");
        Assert.Contains(plan,p => p!["Target"]?["Id"]?.ToString() == submission["Id"]!.ToString());
    }
    [Fact]
    public void CurrentEditedContentKeepsEarlierPublishedSnapshotAndExactDecisionAudit()
    {
        var inv = Fixture(); var m = inv["ModerationReviews"]![0]!.AsObject();
        m["Comment"] = "Edited pending content"; m["RejectedReason"] = "exact"; m["ModeratedBy"] = Guid.NewGuid().ToString();
        m["ModeratedAt"] = "2025-01-02T00:00:00Z";
        var entry = LegacyReviewBackfill.Map(inv)[1]!;
        Assert.Equal(2,entry["Target"]!["ContentRevision"]!.GetValue<int>());
        Assert.Equal("Edited pending content",entry["Target"]!["Note"]!.ToString());
        Assert.Equal("Legacy 2",entry["Snapshots"]![0]!["Comment"]!.ToString());
        Assert.Equal(m["Id"]!.ToString(),entry["Snapshots"]![1]!["Id"]!.ToString());
        Assert.Equal(m["ModeratedBy"]!.ToString(),entry["Snapshots"]![1]!["ModeratedBy"]!.ToString());
        Assert.Equal("2025-01-02T00:00:00Z",entry["Snapshots"]![1]!["ModeratedAt"]!.ToString());
    }
    [Fact]
    public void AmbiguousLinksInvalidVisitsAndOrphanAuthorsAreRetainedWithReasons()
    {
        var inv = Fixture(); inv["Reviews"]![0]!["UserId"] = Guid.NewGuid().ToString();
        var linked = (JsonObject)inv["Reviews"]![1]!.DeepClone();
        linked["Id"] = Guid.NewGuid().ToString(); linked["ShopId"] = linked["CoffeeShopId"]!.DeepClone();
        linked["ReviewId"] = inv["Reviews"]![1]!["Id"]!.DeepClone();
        linked["VisitedAt"] = "2025-01-01T00:00:00Z";
        inv["CheckIns"]!.AsArray().Add(linked);
        var other = linked.DeepClone(); other["Id"] = Guid.NewGuid().ToString(); inv["CheckIns"]!.AsArray().Add(other);
        var original = inv.ToJsonString(); var result = LegacyReviewBackfill.Map(inv);
        Assert.Equal("unresolved",result[0]!["Outcome"]!.ToString());
        Assert.Equal("unresolved",result[1]!["Outcome"]!.ToString());
        Assert.Equal(2,result.Count(r => r!["SourceTable"]!.ToString() == "CheckIns" && r["Outcome"]!.ToString() == "invalid"));
        Assert.Equal(original,inv.ToJsonString());
    }
    [Fact]
    public void NativeTargetCannotBeMistakenForUnchangedStageAndSourceCommandsAreParameterized()
    {
        var row = LegacyReviewBackfill.Map(Fixture())[0]!["Target"]!.AsObject(); row["Note"] = "quote ' ; DELETE;";
        using var insert = LegacyReviewBackfill.InsertCommand("CanonicalCheckIns",LegacyReviewBackfill.Stage(row));
        Assert.DoesNotContain("DELETE;",insert.CommandText);
        Assert.Contains("jsonb_populate_record",insert.CommandText); Assert.Contains("ON CONFLICT DO NOTHING",insert.CommandText);
        Assert.Equal(NpgsqlTypes.NpgsqlDbType.Jsonb,insert.Parameters[0].NpgsqlDbType);
        Assert.Contains("DELETE;",insert.Parameters[0].Value!.ToString());
        Assert.True(LegacyReviewBackfill.Matches(row,(JsonObject)row.DeepClone()));
        var changed = (JsonObject)row.DeepClone(); changed["ContentRevision"] = 2;
        Assert.False(LegacyReviewBackfill.Matches(row,changed)); Assert.False(LegacyReviewBackfill.Matches(LegacyReviewBackfill.Stage(row),changed));
        Assert.Equal(0,LegacyReviewBackfill.Stage(row)["Visibility"]!.GetValue<int>());
        Assert.Null(LegacyReviewBackfill.Stage(row)["ApprovedRevision"]);
        Assert.Throws<ArgumentException>(() => LegacyReviewBackfill.InsertCommand("Reviews",row));
    }
    [Fact]
    public void ExactVotesReportsAndCountsAreIdempotentWithMergedAndIncompleteLegacyVisits()
    {
        var inv = Fixture();
        inv["ReviewReports"]!.AsArray().Add(new JsonObject { ["Id"] = Guid.NewGuid().ToString(), ["ReviewId"] = inv["Reviews"]![0]!["Id"]!.DeepClone(), ["Status"] = 2, ["Text"] = "unchanged audit" });
        inv["ReviewHelpfulVotes"]!.AsArray().Add(new JsonObject { ["ReviewId"] = inv["Reviews"]![0]!["Id"]!.DeepClone(),
            ["UserId"] = inv["Users"]![0]!["Id"]!.DeepClone(), ["CreatedAtUtc"] = "2025-01-02T00:00:00Z" });
        var visit = (JsonObject)inv["Reviews"]![0]!.DeepClone(); visit["Id"] = Guid.NewGuid().ToString();
        visit["ReviewId"] = visit["Id"]!.DeepClone(); visit["ShopId"] = visit["CoffeeShopId"]!.DeepClone(); inv["CheckIns"]!.AsArray().Add(visit);
        var manifest = Manifest(inv); var before = LegacyReviewBackfill.Hash(inv["ReviewReports"]!);
        var targets = manifest["Mappings"]!.AsArray().OfType<JsonObject>().Where(e => e["Target"] != null).Select(e => e["Target"]!.AsObject()).ToArray();
        Assert.Equal(1,targets[0]["HelpfulCount"]!.GetValue<int>()); Assert.Single(manifest["Votes"]!.AsArray());
        Assert.Equal("2025-01-02T00:00:00Z",manifest["Votes"]![0]!["CreatedAtUtc"]!.ToString());
        var counts = LegacyReviewBackfill.Counts(manifest,targets.Concat(targets));
        Assert.Equal(6,counts[inv["Users"]![0]!["Id"]!.ToString()]);
        LegacyReviewBackfill.PrepareVotes(manifest);
        Assert.Single(manifest["Votes"]!.AsArray()); Assert.Equal(before,LegacyReviewBackfill.Hash(inv["ReviewReports"]!));
    }
    [Fact]
    public void HistoricalPhotoSetUsesExactMediaIdentityAndRetainsMoreThanFivePhotos()
    {
        var manifest = Manifest(); var inv = manifest["Inventory"]!.AsObject();
        for (var i=0;i<6;i++)
        {
            var mediaId = Guid.NewGuid().ToString();
            var photo = new JsonObject { ["Id"] = Guid.NewGuid().ToString(), ["ReviewId"] = inv["Reviews"]![0]!["Id"]!.DeepClone(),
                ["StorageKey"] = $"old/{i}.svg", ["OwnerId"] = inv["Users"]![0]!["Id"]!.DeepClone(), ["FileName"] = "old.svg",
                ["ContentType"] = "image/svg+xml", ["SizeBytes"] = 42, ["SortIndex"] = i };
            inv["ShopPhotos"]!.AsArray().Add(photo);
            var media = (JsonObject)photo.DeepClone(); media["Id"] = mediaId; media["OwnerType"] = 1; media["BucketType"] = 1;
            media["Status"] = 1; media["UploadedAt"] = "2025-01-01T00:00:00Z"; inv["Photos"]!.AsArray().Add(media);
            inv["Gallery"]!.AsArray().Add(new JsonObject { ["Service"] = "Shops", ["Table"] = "ShopPhotos", ["Row"] = photo.DeepClone() });
        }
        LegacyReviewBackfill.PreparePhotos(manifest);
        Assert.Equal(6,manifest["Photos"]!.AsArray().Count);
        Assert.All(manifest["Photos"]!.AsArray(),p => Assert.Equal(inv["Users"]![0]!["Id"]!.ToString(),p!["OwnerId"]!.ToString()));
        Assert.Equal(inv["Photos"]![0]!["Id"]!.ToString(),manifest["Photos"]![0]!["MediaId"]!.ToString());
        Assert.Equal(5,manifest["Photos"]![5]!["SortIndex"]!.GetValue<int>());
        Assert.All(manifest["PhotoOutcomes"]!.AsArray(),p => Assert.Equal("mapped",p!["Outcome"]!.ToString()));
    }
    [Theory]
    [InlineData("owner")]
    [InlineData("gallery")]
    [InlineData("missing")]
    public void ConflictingOrMissingHistoricalPhotoEvidenceKeepsVisitNonpublic(string failure)
    {
        var manifest = Manifest(); var inv = manifest["Inventory"]!.AsObject();
        var photo = new JsonObject { ["Id"] = Guid.NewGuid().ToString(), ["ReviewId"] = inv["Reviews"]![0]!["Id"]!.DeepClone(),
            ["StorageKey"] = "old.jpg", ["OwnerId"] = inv["Users"]![0]!["Id"]!.DeepClone(), ["ContentType"] = "image/jpeg", ["SizeBytes"] = 42 };
        inv["ShopPhotos"]!.AsArray().Add(photo);
        if (failure != "missing")
        {
            var media = (JsonObject)photo.DeepClone(); media["Id"] = Guid.NewGuid().ToString(); media["OwnerType"] = 1;
            media["BucketType"] = 1; media["Status"] = 1; media["FileName"] = "old.jpg";
            if (failure == "owner") media["OwnerId"] = Guid.NewGuid().ToString();
            inv["Photos"]!.AsArray().Add(media);
        }
        if (failure == "gallery")
            inv["Gallery"]!.AsArray().Add(new JsonObject { ["Service"] = "Shops", ["Table"] = "CoffeePhotos",
                ["Row"] = new JsonObject { ["Id"] = Guid.NewGuid().ToString(), ["StorageKey"] = "old.jpg" } });
        LegacyReviewBackfill.PreparePhotos(manifest);
        Assert.Empty(manifest["Photos"]!.AsArray());
        Assert.True(manifest["Mappings"]![0]!["PhotosUnresolved"]!.GetValue<bool>());
        Assert.Equal("unresolved",manifest["PhotoOutcomes"]![0]!["Outcome"]!.ToString());
    }


    [Fact]
    public void CountReconciliationRequiresHandledInboxAndEmptyOutgoingIncludingDelayedNativeCreation()
    {
        Assert.False(LegacyReviewBackfill.EnvelopeBlocksCountReconciliation("wolverine_incoming_envelopes",new JsonObject { ["status"]="Handled" }));
        foreach (var status in new[]{"Incoming","Scheduled","Error","unknown"})
            Assert.True(LegacyReviewBackfill.EnvelopeBlocksCountReconciliation("wolverine_incoming_envelopes",new JsonObject { ["status"]=status, ["message_type"]="CheckinCreatedEvent" }));
        Assert.True(LegacyReviewBackfill.EnvelopeBlocksCountReconciliation("wolverine_outgoing_envelopes",new JsonObject()));
        Assert.True(LegacyReviewBackfill.EnvelopeBlocksCountReconciliation("wolverine_dead_letter_envelopes",new JsonObject()));
        var m=Manifest(); var targets=m["Mappings"]!.AsArray().OfType<JsonObject>().Where(e=>e["Target"] != null).Select(e=>e["Target"]!.AsObject()).ToArray();
        var native=(JsonObject)targets[0].DeepClone(); native["Id"]=Guid.NewGuid().ToString();
        Assert.Equal(6,LegacyReviewBackfill.Counts(m,targets.Append(native))[native["UserId"]!.ToString()]);
        Assert.Equal(6,LegacyReviewBackfill.Counts(m,targets.Append(native))[native["UserId"]!.ToString()]);
    }


    [Fact]
    public void ChangedModerationPhotoSetDoesNotRepublishRemovedHistoricalPhotos()
    {
        var inv = Fixture();
        foreach(var key in new[]{"Photos","ShopPhotos","ModerationPhotos","Gallery"}) inv[key] = new JsonArray();
        var published = inv["Reviews"]![1]!.AsObject(); var pending = inv["ModerationReviews"]![0]!.AsObject();
        pending["Comment"] = "Changed with replacement photo";
        var visit = (JsonObject)published.DeepClone(); visit["Id"] = Guid.NewGuid().ToString();
        visit["ShopId"] = published["CoffeeShopId"]!.DeepClone(); visit["ReviewId"] = published["Id"]!.DeepClone();
        visit["VisitedAt"] = "2024-12-20T00:00:00Z"; inv["CheckIns"]!.AsArray().Add(visit);
        void Photo(string service,string key,string parent,string column)
        {
            var photo = new JsonObject { ["Id"] = Guid.NewGuid().ToString(), [column] = parent, ["StorageKey"] = key,
                ["OwnerId"] = published["UserId"]!.DeepClone(), ["ContentType"] = "image/jpeg", ["FileName"] = key, ["SizeBytes"] = 7 };
            inv[service=="Shops" ? "ShopPhotos" : "ModerationPhotos"]!.AsArray().Add(photo);
            inv["Gallery"]!.AsArray().Add(new JsonObject { ["Service"] = service, ["Table"] = "ShopPhotos", ["Row"] = photo.DeepClone() });
            if (!inv["Photos"]!.AsArray().Any(p=>p!["StorageKey"]!.ToString()==key))
            {
                var media = (JsonObject)photo.DeepClone(); media["Id"] = Guid.NewGuid().ToString();
                media["OwnerType"] = 1; media["BucketType"] = 1; media["Status"] = 1; inv["Photos"]!.AsArray().Add(media);
            }
        }
        Photo("Shops","old.jpg",published["Id"]!.ToString(),"ReviewId");
        Photo("Shops","old.jpg",visit["Id"]!.ToString(),"CheckInId");
        Photo("Moderation","new.jpg",pending["Id"]!.ToString(),"ModerationReviewId");
        var manifest = Manifest(inv); var mapped = manifest["Mappings"]![1]!;
        Assert.Equal(2,mapped["Snapshots"]!.AsArray().Count);
        var old = manifest["Photos"]!.AsArray().Single(p=>p!["OriginalKey"]!.ToString()=="old.jpg")!;
        var current = manifest["Photos"]!.AsArray().Single(p=>p!["OriginalKey"]!.ToString()=="new.jpg")!;
        Assert.False(old["Current"]!.GetValue<bool>()); Assert.True(current["Current"]!.GetValue<bool>());
        Assert.Equal(mapped["Snapshots"]![0]!["Id"]!.ToString(),old["Snapshots"]![0]!["SnapshotId"]!.ToString());
        Assert.Equal(mapped["Snapshots"]![1]!["Id"]!.ToString(),current["Snapshots"]![0]!["SnapshotId"]!.ToString());
    }


    [Fact]
    public async Task ActualMainOtherSelectionPreservesReviewAndChangedLinkedSnapshotWithoutCatalogOtherRow()
    {
        var catalog = new Moq.Mock<CoffeePeek.Shared.Kernel.Drinks.IDrinkCatalog>();
        var first = await CoffeePeek.Shared.Kernel.Drinks.DrinkSelection.ResolveAsync("other","Original custom",catalog.Object,TestContext.Current.CancellationToken);
        var current = await CoffeePeek.Shared.Kernel.Drinks.DrinkSelection.ResolveAsync("other","Changed custom",catalog.Object,TestContext.Current.CancellationToken);
        var inv = Fixture();
        void Set(JsonObject row,CoffeePeek.Shared.Kernel.Drinks.DrinkSelection drink)
        { row["DrinkSlug"]=drink.Slug; row["CustomDrinkName"]=drink.CustomName; row["DrinkNameRu"]=drink.NameRu; row["DrinkNameEn"]=drink.NameEn; }
        Set(inv["Reviews"]![0]!.AsObject(),first!);
        Set(inv["Reviews"]![1]!.AsObject(),first!);
        Set(inv["ModerationReviews"]![0]!.AsObject(),current!);
        var plan = LegacyReviewBackfill.Map(inv);
        Assert.Equal("mapped",plan[0]!["Outcome"]!.ToString());
        Assert.Equal("other",plan[0]!["Target"]!["DrinkSlug"]!.ToString());
        Assert.Equal("Original custom",plan[0]!["Target"]!["CustomDrinkName"]!.ToString());
        Assert.Equal("Changed custom",plan[1]!["Target"]!["CustomDrinkName"]!.ToString());
        Assert.Equal("Original custom",plan[1]!["Snapshots"]![0]!["DrinkNameRu"]!.ToString());
        Assert.Equal("Changed custom",plan[1]!["Snapshots"]![1]!["DrinkNameEn"]!.ToString());
        catalog.Verify(c=>c.FindAsync(Moq.It.IsAny<string>(),Moq.It.IsAny<CancellationToken>()),Moq.Times.Never);
    }
    [Theory]
    [InlineData("other",null)]
    [InlineData("other"," ")]
    [InlineData("espresso","custom")]
    [InlineData(null,"custom")]
    public void IncompleteOtherAndCustomNameForNamedOrAbsentSlugRemainUnresolved(string? slug,string? custom)
    {
        var inv=Fixture(); inv["Drinks"]!.AsArray().Add(new JsonObject {["Slug"]="espresso"});
        inv["Reviews"]![0]!["DrinkSlug"]=slug; inv["Reviews"]![0]!["CustomDrinkName"]=custom;
        Assert.Equal("unresolved",LegacyReviewBackfill.Map(inv)[0]!["Outcome"]!.ToString());
        inv["Reviews"]![0]!["DrinkSlug"]="other"; inv["Reviews"]![0]!["CustomDrinkName"]=new string('x',101);
        Assert.Equal("unresolved",LegacyReviewBackfill.Map(inv)[0]!["Outcome"]!.ToString());
    }
    [Theory]
    [InlineData("000123","123")]
    [InlineData("2025-01-01T00:00:00Z","2025-01-01T03:00:00+03:00")]
    [InlineData("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa","AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA")]
    public void AuthoredStringsStayExactInHashesAndRetryComparisons(string before,string after)
    {
        var a=new JsonObject {["Note"]=before}; var b=new JsonObject {["Note"]=after};
        Assert.NotEqual(LegacyReviewBackfill.Hash(a),LegacyReviewBackfill.Hash(b));
        Assert.False(LegacyReviewBackfill.Matches(a,b));
    }
    [Fact]
    public void NullNeverMatchesLiteralSentinelAndOnlyKnownTypedColumnsAllowTimeUuidEquivalence()
    {
        var empty=new JsonObject {["CustomDrinkName"]=null}; var literal=new JsonObject {["CustomDrinkName"]="NULL"};
        Assert.NotEqual(LegacyReviewBackfill.Hash(empty),LegacyReviewBackfill.Hash(literal));
        Assert.False(LegacyReviewBackfill.Matches(empty,literal)); Assert.False(LegacyReviewBackfill.Matches(literal,empty));
        Assert.False(LegacyReviewBackfill.Matches(empty,new JsonObject()));
        Assert.NotEqual(LegacyReviewBackfill.Hash(null!),LegacyReviewBackfill.Hash(System.Text.Json.Nodes.JsonValue.Create("NULL")!));
        var a=new JsonObject {["VisitedAt"]="2025-01-01T00:00:00Z",["Id"]="aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"};
        var b=new JsonObject {["VisitedAt"]="2025-01-01T03:00:00+03:00",["Id"]="AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA"};
        Assert.NotEqual(LegacyReviewBackfill.Hash(a),LegacyReviewBackfill.Hash(b));
        Assert.True(LegacyReviewBackfill.Matches(a,b));
        Assert.Equal(LegacyReviewBackfill.Hash(JsonNode.Parse("{\"Count\":1}")!),LegacyReviewBackfill.Hash(JsonNode.Parse("{\"Count\":1.0}")!));
        Assert.NotEqual(LegacyReviewBackfill.Hash(JsonNode.Parse("{\"Count\":1}")!),LegacyReviewBackfill.Hash(JsonNode.Parse("{\"Count\":\"1\"}")!));
    }

}