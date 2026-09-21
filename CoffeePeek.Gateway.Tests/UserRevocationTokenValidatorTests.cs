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
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_revokedUsers.Contains(userId));
    }
}
