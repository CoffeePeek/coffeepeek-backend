namespace CoffeePeek.Shops.Infrastructure.Tests.LivePostgres;

public sealed record ReadPerformanceResult(string Scenario, int Clients, int Samples,
    double P50Ms, double P95Ms, double P99Ms, int MinSql, int MaxSql,
    long BatchAllocatedBytes, long WorkingSetBytes);
