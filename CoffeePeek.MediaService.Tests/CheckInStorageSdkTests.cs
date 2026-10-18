using System.Net;
using CoffeePeek.MediaService.Configuration;
using CoffeePeek.MediaService.Services;
using Minio;

namespace CoffeePeek.MediaService.Tests;

/// <summary>Exercise the pinned SDK response handling, not just an IMinioClient mock.</summary>
public class CheckInStorageSdkTests
{
    [Fact]
    public async Task MissingPolicyResponseIsPrivateButMissingBucketOrAccessDeniedIsNot()
    {
        var handler = new StorageHandler();
        using var http = new HttpClient(handler);
        var client = new MinioClient().WithEndpoint("storage.test:9000").WithCredentials("test-reader", "test-secret")
            .WithRegion("us-east-1").WithHttpClient(http).WithSSL(false).Build();
        using var storage = new MinIOStorageService(new MinIOOptions(), client);
        var ct = TestContext.Current.CancellationToken;
        Assert.True(await storage.IsCheckInBucketPrivateAsync(ct));
        Assert.Equal(new[] { "HEAD ", "GET ?policy=" }, handler.Requests);
        handler.BucketExists = false;
        Assert.False(await storage.IsCheckInBucketPrivateAsync(ct));
        handler.BucketExists = true;
        handler.ErrorCode = "AccessDenied";
        await Assert.ThrowsAnyAsync<Exception>(() => storage.IsCheckInBucketPrivateAsync(ct));
    }

    private sealed class StorageHandler : HttpMessageHandler
    {
        public bool BucketExists { get; set; } = true;
        public string ErrorCode { get; set; } = "NoSuchBucketPolicy";
        public List<string> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.Method + " " + request.RequestUri!.Query);
            var response = request.Method == HttpMethod.Head
                ? new HttpResponseMessage(BucketExists ? HttpStatusCode.OK : HttpStatusCode.NotFound)
                : new HttpResponseMessage(ErrorCode == "NoSuchBucketPolicy" ? HttpStatusCode.NotFound : HttpStatusCode.Forbidden)
                {
                    Content = new StringContent($"<Error><Code>{ErrorCode}</Code><Message>test</Message><Resource>/coffeepeek.checkins</Resource><RequestId>test</RequestId></Error>",
                        System.Text.Encoding.UTF8, "application/xml")
                };
            response.RequestMessage = request;
            response.Headers.Add("x-amz-request-id", "test-request");
            return Task.FromResult(response);
        }
    }
}
