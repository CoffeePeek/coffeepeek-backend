using System.Net;
using System.Security.Cryptography;
using System.Text;
using CoffeePeek.Account.Infrastructure.Identity;
using CoffeePeek.Account.Application.Common.Interfaces;
using CoffeePeek.Account.Application.Features.Auth.AppleLogin;
using CoffeePeek.Shared.Kernel.Exceptions;
using FluentAssertions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Moq.Protected;
using StackExchange.Redis;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace CoffeePeek.Account.Infrastructure.Tests.Identity;

public sealed class AppleWebAuthServiceTests : IDisposable
{
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly Mock<IAppleLoginChallengeStore> _challenges = new();
    private readonly Guid _challengeId = Guid.NewGuid();
    private readonly DateTimeOffset _challengeExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5);
    private readonly string _nonce = "server-generated-challenge-nonce";
    private readonly Mock<IDatabase> _db = new();
    private readonly Mock<HttpMessageHandler> _http = new();
    private readonly Dictionary<string, string> _states = [];
    private readonly HttpClient _client;
    private readonly OAuthAppleOptions _options;
    private string? _postedForm;
    private int _httpCalls;
    private string _responseBody = "{\"id_token\":\"apple-id-token\"}";
    private HttpStatusCode _status = HttpStatusCode.OK;

    public AppleWebAuthServiceTests()
    {
        _challenges.Setup(c => c.CreateAsync(It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult(new AppleLoginChallenge(_challengeId, _nonce, _challengeExpiresAt)));
        _options = new OAuthAppleOptions
        {
            ServiceId = "by.coffeepeek.auth", TeamId = "test-team", KeyId = "test-key",
            PrivateKeyPem = _key.ExportPkcs8PrivateKeyPem().Replace("\n", "\\n"),
            RedirectUri = "https://api.coffeepeek.by/api/tokens/apple/callback"
        };
        _db.Setup(d => d.StringSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<Expiration>(),
                When.NotExists, CommandFlags.None))
            .Returns((RedisKey key, RedisValue value, Expiration expiration, ValueCondition _, CommandFlags _) =>
            {
                expiration.Should().Be(new Expiration(_challengeExpiresAt.UtcDateTime));
                value.ToString().Should().Be(_challengeId.ToString("N"));
                return Task.FromResult(_states.TryAdd(key.ToString(), value.ToString()));
            });
        _db.Setup(d => d.StringGetDeleteAsync(It.IsAny<RedisKey>(), CommandFlags.None))
            .Returns((RedisKey key, CommandFlags _) => Task.FromResult(
                _states.Remove(key.ToString(), out var value) ? (RedisValue)value : RedisValue.Null));
        _http.Protected().Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns(async (HttpRequestMessage request, CancellationToken ct) =>
            {
                _httpCalls++;
                request.RequestUri!.ToString().Should().Be("https://appleid.apple.com/auth/token");
                request.Method.Should().Be(HttpMethod.Post);
                request.Content!.Headers.ContentType!.MediaType.Should().Be("application/x-www-form-urlencoded");
                _postedForm = await request.Content.ReadAsStringAsync(ct);
                return new HttpResponseMessage(_status) { Content = new StringContent(_responseBody, Encoding.UTF8, "application/json") };
            });
        _client = new HttpClient(_http.Object);
    }

    private AppleWebAuthService CreateSut()
    {
        var redis = new Mock<IConnectionMultiplexer>();
        redis.Setup(r => r.GetDatabase(-1, null)).Returns(_db.Object);
        return new AppleWebAuthService(_client, redis.Object, _challenges.Object, MsOptions.Create(_options), NullLogger<AppleWebAuthService>.Instance);
    }

    [Fact]
    public async Task Authorize_UsesServerChallengeNonceAndCodeExchangePreservesChallengeAndSignsClientSecret()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = CreateSut();
        var start = await service.CreateAuthorizationAsync(ct);
        var query = QueryHelpers.ParseQuery(new Uri(start.AuthorizationUrl).Query);
        query["response_type"].ToString().Should().Be("code");
        query["response_mode"].ToString().Should().Be("form_post");
        query["scope"].ToString().Should().Be("email");
        query["client_id"].ToString().Should().Be(_options.ServiceId);
        query["redirect_uri"].ToString().Should().Be(_options.RedirectUri);
        query["state"].ToString().Should().Be(start.State);
        start.State.Length.Should().Be(64);
        start.ExpiresAt.Should().Be(_challengeExpiresAt);
        query["nonce"].ToString().Should().Be(_nonce);
        query["nonce"].ToString().Should().NotBe(start.State);
        start.ToString().Should().NotContain(start.State).And.NotContain(_nonce);
        _challenges.Verify(c => c.CreateAsync(ct), Times.Once);

        var command = await service.ExchangeCodeAsync("single-use-code", start.State, ct);
        command!.IdToken.Should().Be("apple-id-token");
        command.ChallengeId.Should().Be(_challengeId);
        command.ToString().Should().NotContain("apple-id-token");
        _states.Should().BeEmpty();
        var form = QueryHelpers.ParseQuery(_postedForm!);
        form["code"].ToString().Should().Be("single-use-code");
        form["grant_type"].ToString().Should().Be("authorization_code");
        form["client_id"].ToString().Should().Be(_options.ServiceId);
        form["redirect_uri"].ToString().Should().Be(_options.RedirectUri);
        var secret = form["client_secret"].ToString();
        var token = new JsonWebToken(secret);
        token.Alg.Should().Be("ES256");
        token.Kid.Should().Be(_options.KeyId);
        token.Subject.Should().Be(_options.ServiceId);
        token.ValidTo.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(5), TimeSpan.FromSeconds(5));
        var validation = await new JsonWebTokenHandler().ValidateTokenAsync(secret, new TokenValidationParameters
        {
            ValidIssuer = _options.TeamId, ValidAudience = "https://appleid.apple.com",
            IssuerSigningKey = new ECDsaSecurityKey(_key), ValidAlgorithms = [SecurityAlgorithms.EcdsaSha256],
            ValidateIssuerSigningKey = true
        });
        validation.IsValid.Should().BeTrue();

        (await service.ExchangeCodeAsync("single-use-code", start.State, ct)).Should().BeNull();
        _httpCalls.Should().Be(1);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("unknown")]
    [InlineData("expired")]
    [InlineData("cancelled")]
    public async Task MissingOrCancelledState_DoesNotContactApple(string scenario)
    {
        var service = CreateSut();
        var start = await service.CreateAuthorizationAsync(TestContext.Current.CancellationToken);
        if (scenario == "expired") _states.Clear();
        var result = await service.ExchangeCodeAsync(scenario == "cancelled" ? null : "code",
            scenario == "invalid" ? "bad-state" : scenario == "unknown" ? new string('a', 64) : start.State, TestContext.Current.CancellationToken);
        result.Should().BeNull();
        _httpCalls.Should().Be(0);
        if (scenario == "cancelled") _states.Should().BeEmpty();
    }

    [Theory]
    [InlineData(400, "{\"error\":\"invalid_grant\"}")]
    [InlineData(200, "{}")]
    [InlineData(200, "{\"id_token\":42}")]
    [InlineData(200, "{\"id_token\":\"\"}")]
    [InlineData(200, "invalid-json")]
    public async Task FailedExchange_ConsumesStateWithoutProducingLogin(int status, string body)
    {
        _status = (HttpStatusCode)status;
        _responseBody = body;
        var service = CreateSut();
        var start = await service.CreateAuthorizationAsync(TestContext.Current.CancellationToken);
        (await service.ExchangeCodeAsync("code", start.State, TestContext.Current.CancellationToken)).Should().BeNull();
        _states.Should().BeEmpty();
    }

    [Theory]
    [InlineData(429)]
    [InlineData(503)]
    public async Task UpstreamOutage_ReturnsServiceUnavailable(int status)
    {
        _status = (HttpStatusCode)status;
        var service = CreateSut();
        var start = await service.CreateAuthorizationAsync(TestContext.Current.CancellationToken);
        var act = () => service.ExchangeCodeAsync("code", start.State, TestContext.Current.CancellationToken);
        await act.Should().ThrowAsync<ServiceUnavailableException>();
        _states.Should().BeEmpty();
    }

    [Theory]
    [InlineData("http://api.coffeepeek.by/api/tokens/apple/callback")]
    [InlineData("https://api.coffeepeek.by/api/auth/apple/callback")]
    [InlineData("https://localhost/api/tokens/apple/callback")]
    public async Task WrongCallbackConfiguration_FailsBeforeStartingFlow(string redirect)
    {
        _options.RedirectUri = redirect;
        var act = () => CreateSut().CreateAuthorizationAsync(TestContext.Current.CancellationToken);
        await act.Should().ThrowAsync<ServiceUnavailableException>();
        _states.Should().BeEmpty();
    }

    [Fact]
    public async Task InvalidPrivateKey_FailsWithoutSendingSecrets()
    {
        _options.PrivateKeyPem = "invalid-pem";
        var service = CreateSut();
        var start = await service.CreateAuthorizationAsync(TestContext.Current.CancellationToken);
        var act = () => service.ExchangeCodeAsync("code", start.State, TestContext.Current.CancellationToken);
        await act.Should().ThrowAsync<ServiceUnavailableException>();
        _httpCalls.Should().Be(0);
    }

    public void Dispose()
    {
        _client.Dispose();
        _key.Dispose();
    }
}
