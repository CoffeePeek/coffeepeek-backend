using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using CoffeePeek.Account.Application.Common.Interfaces;
using CoffeePeek.Account.Application.Common.Models;
using CoffeePeek.Account.Application.Features.Auth.AppleLogin;
using CoffeePeek.Shared.Kernel.Exceptions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;

namespace CoffeePeek.Account.Infrastructure.Identity;

public sealed class AppleWebAuthService(
    HttpClient httpClient,
    IConnectionMultiplexer redis,
    IAppleLoginChallengeStore challenges,
    IOptions<OAuthAppleOptions> options,
    ILogger<AppleWebAuthService> logger) : IAppleWebAuthService
{
    private const string AppleIssuer = "https://appleid.apple.com";

    public async Task<AppleAuthorizationRequest> CreateAuthorizationAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ValidateConfiguration();
        var state = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var challenge = await challenges.CreateAsync(ct);
        var expiresAt = challenge.ExpiresAt;
        var maximumExpiry = DateTimeOffset.UtcNow.AddMinutes(5);
        if (expiresAt > maximumExpiry) expiresAt = maximumExpiry;
        if (expiresAt <= DateTimeOffset.UtcNow)
            throw new ServiceUnavailableException("Could not start Apple sign-in. Please retry.");
        var stored = await redis.GetDatabase().StringSetAsync(StateKey(state), challenge.ChallengeId.ToString("N"),
            new Expiration(expiresAt.UtcDateTime), When.NotExists).WaitAsync(ct);
        if (!stored)
            throw new ServiceUnavailableException("Could not start Apple sign-in. Please retry.");

        var url = QueryHelpers.AddQueryString(AppleIssuer + "/auth/authorize",
            new Dictionary<string, string?>
            {
                ["client_id"] = options.Value.ServiceId,
                ["redirect_uri"] = options.Value.RedirectUri,
                ["response_type"] = "code",
                ["response_mode"] = "form_post",
                ["scope"] = "email",
                ["state"] = state,
                ["nonce"] = challenge.Nonce
            });
        return new AppleAuthorizationRequest(url, state, expiresAt);
    }

    public async Task<AppleLoginCommand?> ExchangeCodeAsync(string? code, string state, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ValidateConfiguration();
        if (string.IsNullOrWhiteSpace(state) || state.Length != 64 || state.Any(c => !char.IsAsciiHexDigit(c)))
            return null;

        // GETDEL consumes state atomically, including cancelled and failed login attempts.
        var storedChallengeId = await redis.GetDatabase().StringGetDeleteAsync(StateKey(state)).WaitAsync(ct);
        if (!Guid.TryParseExact(storedChallengeId.ToString(), "N", out var challengeId) || string.IsNullOrWhiteSpace(code) || code.Length > 4096)
            return null;

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = options.Value.ServiceId,
            ["client_secret"] = CreateClientSecret(),
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = options.Value.RedirectUri
        });

        try
        {
            using var response = await httpClient.PostAsync(AppleIssuer + "/auth/token", content, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Apple code exchange failed with HTTP {StatusCode}", (int)response.StatusCode);
                if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests)
                    throw new ServiceUnavailableException("Apple sign-in is temporarily unavailable.");
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            if (payload.ValueKind != JsonValueKind.Object ||
                !payload.TryGetProperty("id_token", out var token) || token.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(token.GetString()) || token.GetString()!.Length > 16384)
                return null;

            return new AppleLoginCommand(challengeId, token.GetString()!);
        }
        catch (HttpRequestException)
        {
            throw new ServiceUnavailableException("Apple sign-in is temporarily unavailable.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ServiceUnavailableException("Apple sign-in timed out. Please restart sign-in.");
        }
        catch (JsonException)
        {
            logger.LogWarning("Apple code exchange returned an invalid token response");
            return null;
        }
    }

    private string CreateClientSecret()
    {
        using var signingKey = ECDsa.Create();
        try
        {
            signingKey.ImportFromPem(options.Value.PrivateKeyPem.Replace("\\n", "\n"));
            var key = new ECDsaSecurityKey(signingKey)
            {
                KeyId = options.Value.KeyId,
                CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false }
            };
            var now = DateTime.UtcNow;
            return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(new SecurityTokenDescriptor
            {
                Issuer = options.Value.TeamId,
                Audience = AppleIssuer,
                Claims = new Dictionary<string, object> { ["sub"] = options.Value.ServiceId },
                IssuedAt = now,
                Expires = now.AddMinutes(5),
                SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.EcdsaSha256)
            });
        }
        catch (Exception ex) when (ex is ArgumentException or CryptographicException)
        {
            throw new ServiceUnavailableException("Apple signing key is not configured correctly.");
        }
    }

    private void ValidateConfiguration()
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.ServiceId) || string.IsNullOrWhiteSpace(settings.TeamId) ||
            string.IsNullOrWhiteSpace(settings.KeyId) || string.IsNullOrWhiteSpace(settings.PrivateKeyPem) ||
            !Uri.TryCreate(settings.RedirectUri, UriKind.Absolute, out var redirect) ||
            redirect.Scheme != Uri.UriSchemeHttps || redirect.IsLoopback ||
            redirect.HostNameType != UriHostNameType.Dns || !string.IsNullOrEmpty(redirect.UserInfo) ||
            !string.IsNullOrEmpty(redirect.Fragment) || !string.IsNullOrEmpty(redirect.Query) ||
            redirect.AbsolutePath != "/api/tokens/apple/callback")
            throw new ServiceUnavailableException("Apple web sign-in is not configured.");
    }

    private static string StateKey(string state) => "auth:apple:state:" + state;
}
