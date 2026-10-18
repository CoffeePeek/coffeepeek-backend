using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using CoffeePeek.Contract.Dtos.CheckIns;
using CoffeePeek.Shared.Auth;
using CoffeePeek.Shared.Auth.Options;
using CoffeePeek.Shared.Kernel.Exceptions;
using Microsoft.Extensions.Options;

namespace CoffeePeek.MediaService.Services.Backfill;

public sealed class PhotoBackfillCandidateClient(HttpClient http, IOptions<GatewayAuthOptions> auth)
    : IPhotoBackfillCandidateSource
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter() } };

    public async Task<PhotoBackfillPage> ReadAsync(PhotoBackfillPageRequest page, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/check-in-photo-backfill/candidates")
            { Content = JsonContent.Create(page, options: Json) };
            request.AddGatewayAuthHeader(auth.Value.SecretKey);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > 1024 * 1024) throw new InvalidDataException();
            await using var body = await response.Content.ReadAsStreamAsync(ct);
            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = await body.ReadAsync(chunk, ct)) != 0)
            {
                if (buffer.Length + read > 1024 * 1024) throw new InvalidDataException();
                buffer.Write(chunk, 0, read);
            }
            var result = JsonSerializer.Deserialize<PhotoBackfillPage>(buffer.GetBuffer().AsSpan(0, (int)buffer.Length), Json);
            if (result?.Items is null || result.Items.Length > page.Limit || result.Items.Any(x => x is null))
                throw new InvalidDataException();
            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            throw new ServiceUnavailableException("Community photo inventory is unavailable.");
        }
    }
}
