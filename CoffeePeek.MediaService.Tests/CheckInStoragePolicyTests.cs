using CoffeePeek.MediaService.Services;
using FluentAssertions;
using System.Text.Json.Nodes;

namespace CoffeePeek.MediaService.Tests;

public class CheckInStoragePolicyTests
{
    [Fact]
    public void OldObjectDownloadDeny_PreservesGalleryAccessAndIsIdempotent()
    {
        const string original = "{\"Version\":\"2012-10-17\",\"Statement\":[{\"Sid\":\"gallery\",\"Effect\":\"Allow\",\"Principal\":\"*\",\"Action\":[\"s3:GetObject\"],\"Resource\":[\"arn:aws:s3:::shops/*\"]}]}";
        var first = MinIOStorageService.BuildObjectDenyPolicy(original, "shops", "reviews/old.jpg");
        var second = MinIOStorageService.BuildObjectDenyPolicy(first, "shops", "reviews/old.jpg");
        second.Should().Be(first);
        var statements = JsonNode.Parse(second)!["Statement"]!.AsArray();
        statements.Should().HaveCount(2);
        statements[0]!["Sid"]!.GetValue<string>().Should().Be("gallery");
        statements[1]!["Effect"]!.GetValue<string>().Should().Be("Deny");
        statements[1]!["Resource"]![0]!.GetValue<string>().Should().Be("arn:aws:s3:::shops/reviews/old.jpg");
    }
}
