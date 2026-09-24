using System.Security.Claims;
using CoffeePeek.Gateway.Authentication;
using CoffeePeek.Shared.Auth;
using FluentAssertions;

namespace CoffeePeek.Gateway.Tests;

public class UserRevocationTokenValidatorTests
{
    [Fact]
    public async Task IsAcceptedAsync_WhenUserIsRevoked_RejectsToken()
    {
        var userId = Guid.NewGuid();
        var validator = new UserRevocationTokenValidator(new StubRevokedUserStore(userId));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", userId.ToString())],
            "Bearer"));

        var accepted = await validator.IsAcceptedAsync(principal);

        accepted.Should().BeFalse();
    }

    [Fact]
    public async Task IsAcceptedAsync_WhenUserIsNotRevoked_AcceptsToken()
    {
        var userId = Guid.NewGuid();
        var validator = new UserRevocationTokenValidator(new StubRevokedUserStore());
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
            "Bearer"));

        var accepted = await validator.IsAcceptedAsync(principal);

        accepted.Should().BeTrue();
    }

    [Fact]
    public async Task IsAcceptedAsync_WhenSubjectIsMissing_RejectsToken()
    {
        var validator = new UserRevocationTokenValidator(new StubRevokedUserStore());

        var accepted = await validator.IsAcceptedAsync(new ClaimsPrincipal());

        accepted.Should().BeFalse();
    }

    [Theory]
    [InlineData(null, 1_000L, false)]   // no marker
    [InlineData("1", 5_000L, true)]     // legacy marker revokes everything
    [InlineData("2000", null, true)]   // token without iat predates the claim
    [InlineData("2000", 1_999L, true)]  // issued before revocation
    [InlineData("2000", 2_001L, false)] // re-login after revocation must work
    public void IsTokenRevoked_ComparesIssuedAtWithRevocationTime(string? marker, long? iat, bool expected)
    {
        DateTimeOffset? issuedAt = iat is null ? null : DateTimeOffset.FromUnixTimeSeconds(iat.Value);

        RevokedUserKeys.IsTokenRevoked(marker, issuedAt).Should().Be(expected);
    }

    [Fact]
    public async Task IsAcceptedAsync_PassesIatClaimToStore()
    {
        var userId = Guid.NewGuid();
        var store = new RecordingRevokedUserStore();
        var validator = new UserRevocationTokenValidator(store);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", userId.ToString()), new Claim("iat", "1700000000")],
            "Bearer"));

        await validator.IsAcceptedAsync(principal);

        store.LastIssuedAt.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1700000000));
    }

    private sealed class RecordingRevokedUserStore : IRevokedUserStore
    {
        public DateTimeOffset? LastIssuedAt { get; private set; }

        public Task<bool> RevokeAsync(Guid userId, TimeSpan ttl, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<bool> IsRevokedAsync(Guid userId, DateTimeOffset? issuedAt, CancellationToken cancellationToken = default)
        {
            LastIssuedAt = issuedAt;
            return Task.FromResult(false);
        }
    }

    private sealed class StubRevokedUserStore(params Guid[] revokedUsers) : IRevokedUserStore
    {
        private readonly HashSet<Guid> _revokedUsers = [..revokedUsers];

        public Task<bool> RevokeAsync(
            Guid userId,
            TimeSpan ttl,
            CancellationToken cancellationToken = default)
        {
            _revokedUsers.Add(userId);
            return Task.FromResult(true);
        }

        public Task<bool> IsRevokedAsync(
            Guid userId,
            DateTimeOffset? issuedAt,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_revokedUsers.Contains(userId));
    }
}
