using System.Security.Cryptography;
using CoffeePeek.Account.Application.Common.Interfaces;
using CoffeePeek.Account.Application.Features.Auth.AppleLogin;
using StackExchange.Redis;

namespace CoffeePeek.Account.Infrastructure.Identity;

public sealed class AppleLoginChallengeStore(IConnectionMultiplexer redis) : IAppleLoginChallengeStore
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    private static string Key(Guid id) => $"auth:apple:challenge:{id:N}";

    public async Task<AppleLoginChallenge> CreateAsync(CancellationToken ct)
    {
        var challenge = new AppleLoginChallenge(Guid.NewGuid(), Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            DateTimeOffset.UtcNow.Add(Lifetime));
        await redis.GetDatabase().StringSetAsync(Key(challenge.ChallengeId), challenge.Nonce, Lifetime).WaitAsync(ct);
        return challenge;
    }

    public async Task<string?> GetNonceAsync(Guid challengeId, CancellationToken ct)
    {
        var value = await redis.GetDatabase().StringGetAsync(Key(challengeId)).WaitAsync(ct);
        return value.IsNull ? null : value.ToString();
    }

    public async Task<bool> ConsumeAsync(Guid challengeId, string nonce, CancellationToken ct)
    {
        // Compare and delete atomically across all AccountService instances.
        const string script = "if redis.call('GET', KEYS[1]) == ARGV[1] then return redis.call('DEL', KEYS[1]) else return 0 end";
        var result = await redis.GetDatabase().ScriptEvaluateAsync(script, [Key(challengeId)], [nonce]).WaitAsync(ct);
        return (long)result == 1;
    }
}
