namespace CoffeePeek.Shared.Domain.PublicAddresses;

/// <summary>
/// PostgreSQL check-constraint SQL shared by every public-address registry (Shops and Account), so the registries
/// cannot drift apart. Changing a value here requires a migration in each service.
/// </summary>
public static class PublicAddressChecks
{
    public const string Slug = "\"Slug\" ~ '^[a-z0-9]+(-[a-z0-9]+)*$' AND length(\"Slug\") BETWEEN 1 AND 100";
    public const string Lifecycle = "(\"State\" IN (1, 2) AND \"OwnerId\" IS NOT NULL) OR (\"State\" = 3 AND \"OwnerId\" IS NULL)";
    public const string Revision = "\"Revision\" > 0";
    public const string Timestamps = "\"UpdatedAtUtc\" >= \"CreatedAtUtc\"";
}
