using CoffeePeek.MediaService.Services;
using FluentAssertions;
using System.Text.Json.Nodes;
using CoffeePeek.MediaService.Configuration;
using Minio;
using Minio.DataModel.Args;
using Moq;

namespace CoffeePeek.MediaService.Tests;

public class CheckInStoragePolicyTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("{\"Version\":\"2012-10-17\",\"Statement\":[]}", true)]
    [InlineData("{\"Statement\":[{\"Effect\":\"Allow\"}]}", false)]
    [InlineData("{}", false)]
    public async Task AuditOnlyReadsPolicy_AndRejectsUnexpectedPolicies(string? policy, bool expected)
    {
        var ct = TestContext.Current.CancellationToken;
        var client = new Mock<IMinioClient>();
        client.Setup(c => c.BucketExistsAsync(It.IsAny<BucketExistsArgs>(), ct)).ReturnsAsync(true);
        client.Setup(c => c.GetPolicyAsync(It.IsAny<GetPolicyArgs>(), ct)).ReturnsAsync(policy!);
        using var storage = new MinIOStorageService(new MinIOOptions(), client.Object);
        Assert.Equal(expected, await storage.IsCheckInBucketPrivateAsync(ct));
        client.Verify(c => c.BucketExistsAsync(It.IsAny<BucketExistsArgs>(), ct), Times.Once);
        client.Verify(c => c.GetPolicyAsync(It.IsAny<GetPolicyArgs>(), ct), Times.Once);
        client.VerifyNoOtherCalls();
    }

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
