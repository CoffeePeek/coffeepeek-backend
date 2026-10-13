using System.Security.Cryptography;
using CoffeePeek.Account.Infrastructure.Identity;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace CoffeePeek.Account.Infrastructure.Tests.Identity;

public sealed class AppleAuthServiceTests : IDisposable
{
    private readonly RSA rsa = RSA.Create(2048);
    private RsaSecurityKey Key => new(rsa) { KeyId = "apple-test-key" };

    private AppleAuthService Service()
    {
        var configuration = new OpenIdConnectConfiguration();
        configuration.SigningKeys.Add(Key);
        return new(Microsoft.Extensions.Options.Options.Create(new OAuthAppleOptions()), new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration));
    }

    private string Token(string issuer = "https://appleid.apple.com", string audience = "com.coffeepeek",
        string nonce = "one-time-nonce", bool expired = false, bool signed = true, bool subject = true, object? verified = null)
    {
        var claims = new Dictionary<string, object>
        {
            ["email"] = "random@privaterelay.appleid.com", ["email_verified"] = verified ?? "true", ["nonce"] = nonce
        };
        if (subject) claims["sub"] = "stable-apple-subject";
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer, Audience = audience, Claims = claims,
            IssuedAt = DateTime.UtcNow.AddMinutes(-20), NotBefore = DateTime.UtcNow.AddMinutes(-20),
            Expires = DateTime.UtcNow.AddMinutes(expired ? -5 : 5),
            SigningCredentials = signed ? new SigningCredentials(Key, SecurityAlgorithms.RsaSha256) : null
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData("true")]
    public async Task ValidSignedToken_AcceptsVerifiedRelayEmail(object verified)
    {
        var result = await Service().ValidateIdTokenAsync(Token(verified: verified), "one-time-nonce", TestContext.Current.CancellationToken);
        result!.Subject.Should().Be("stable-apple-subject");
        result.Email.Should().Be("random@privaterelay.appleid.com");
        result.EmailVerified.Should().BeTrue();
    }

    [Theory]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("nonce")]
    [InlineData("expiry")]
    [InlineData("unsigned")]
    [InlineData("subject")]
    [InlineData("malformed")]
    [InlineData("oversized")]
    public async Task InvalidToken_IsRejected(string kind)
    {
        var token = kind switch
        {
            "issuer" => Token(issuer: "https://attacker.example"),
            "audience" => Token(audience: "another.app"),
            "nonce" => Token(nonce: "other-attempt"),
            "expiry" => Token(expired: true),
            "unsigned" => Token(signed: false),
            "subject" => Token(subject: false),
            "oversized" => new string('a', 16385),
            _ => "not-a-token"
        };
        (await Service().ValidateIdTokenAsync(token, "one-time-nonce", TestContext.Current.CancellationToken)).Should().BeNull();
    }

    [Fact]
    public async Task ForeignSignature_IsRejected()
    {
        using var other = new AppleAuthServiceTests();
        (await Service().ValidateIdTokenAsync(other.Token(), "one-time-nonce", TestContext.Current.CancellationToken)).Should().BeNull();
    }

    public void Dispose() => rsa.Dispose();
}
