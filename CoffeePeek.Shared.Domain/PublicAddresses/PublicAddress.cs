namespace CoffeePeek.Shared.Domain.PublicAddresses;

/// <summary>A reserved address. Multi-row rename and owner-wide revision checks belong to the transaction coordinator.</summary>
public abstract class PublicAddress
{
    public Guid Id { get; private set; }
    public Guid? OwnerId { get; private set; }
    public string Slug { get; private set; } = null!;
    public PublicAddressState State { get; private set; }
    public long Revision { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    protected PublicAddress() { }

    protected PublicAddress(Guid ownerId, string slug, long revision, DateTime now)
    {
        if (ownerId == Guid.Empty) throw new ArgumentException("Owner is required.", nameof(ownerId));
        ArgumentOutOfRangeException.ThrowIfLessThan(revision, 1);
        RequireUtc(now);
        Id = Guid.NewGuid();
        OwnerId = ownerId;
        Slug = PublicSlug.Validate(slug);
        State = PublicAddressState.Canonical;
        Revision = revision;
        CreatedAtUtc = UpdatedAtUtc = now;
    }

    public void MakeAlias(long revision, DateTime now) => Transition(PublicAddressState.Canonical, PublicAddressState.Alias, revision, now);
    public void Restore(long revision, DateTime now) => Transition(PublicAddressState.Alias, PublicAddressState.Canonical, revision, now);

    /// <summary>Irreversible retirement retains the namespace reservation and releases the restrictive owner FK.</summary>
    public void Retire(long revision, DateTime now)
    {
        if (State == PublicAddressState.Tombstone) throw new InvalidOperationException("Address is already retired.");
        Transition(State, PublicAddressState.Tombstone, revision, now);
        OwnerId = null;
    }

    private void Transition(PublicAddressState expected, PublicAddressState next, long revision, DateTime now)
    {
        if (State != expected) throw new InvalidOperationException("Invalid address transition.");
        if (revision <= Revision) throw new ArgumentOutOfRangeException(nameof(revision), "Revision must increase.");
        RequireUtc(now);
        State = next;
        Revision = revision;
        // Clamp rather than throw: a server clock stepping backwards must not block deletion/retirement (500),
        // and timestamps stay monotonic.
        UpdatedAtUtc = now < UpdatedAtUtc ? UpdatedAtUtc : now;
    }

    private static void RequireUtc(DateTime now)
    {
        if (now.Kind != DateTimeKind.Utc) throw new ArgumentException("UTC timestamp required.", nameof(now));
    }
}
