using System.Text.Json.Nodes;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;

namespace CoffeePeek.MediaService.Services;

internal static class MinioClients
{
    // ponytail: region pinned to MinIO's default; read it from config if a deployment sets MINIO_REGION.
    // A pinned region also keeps GetObject-only identities from needing bucket-location requests.
    public static IMinioClient Create(string endpoint, string accessKey, string secretKey)
    {
        var uri = new Uri(endpoint);
        return new MinioClient().WithEndpoint(uri).WithCredentials(accessKey, secretKey)
            .WithRegion("us-east-1").WithSSL(uri.Scheme == Uri.UriSchemeHttps).Build();
    }

    /// <summary>Provisioning removes all bucket policies. Any unexpected policy needs operator inspection.</summary>
    public static async Task<bool> HasNoPolicyAsync(IMinioClient client, string bucket, CancellationToken ct)
    {
        string? policy;
        try { policy = await client.GetPolicyAsync(new GetPolicyArgs().WithBucket(bucket), ct); }
        catch (MinioException error) when (error.Response?.Code == "NoSuchBucketPolicy") { return true; }
        return string.IsNullOrWhiteSpace(policy) || JsonNode.Parse(policy)?["Statement"] is JsonArray { Count: 0 };
    }
}
