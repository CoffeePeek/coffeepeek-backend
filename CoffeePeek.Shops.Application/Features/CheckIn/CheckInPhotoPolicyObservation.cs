namespace CoffeePeek.Shops.Application.Features.CheckIn;

/// <summary>Origin of a policy observation; cache hits and 304 responses must preserve it.</summary>
public sealed record CheckInPhotoPolicyObservation(DateTimeOffset ObservedAtUtc, long Timestamp)
{
    public static readonly TimeSpan FactsLifetime = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan ResponseLifetime = TimeSpan.FromSeconds(20);
    public static readonly TimeSpan DecisionLifetime = FactsLifetime + ResponseLifetime;
    public DateTimeOffset AllowedUntilUtc => ObservedAtUtc + DecisionLifetime;

    public static CheckInPhotoPolicyObservation Start(TimeProvider clock) => new(clock.GetUtcNow(), clock.GetTimestamp());

    // A wall-clock adjustment in either direction cannot extend a locally observed grant.
    public TimeSpan Age(TimeProvider clock) => TimeSpan.FromTicks(Math.Max(
        (clock.GetUtcNow() - ObservedAtUtc).Ticks, clock.GetElapsedTime(Timestamp).Ticks));
    public TimeSpan Remaining(TimeProvider clock) => DecisionLifetime - Age(clock);
}
